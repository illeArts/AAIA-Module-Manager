using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Messaging;
using AAIA.Air.Persistence;

namespace AAIA.ModuleManager.Services.Ai.Persistence;

public enum AiFileStateStoreFaultPoint
{
    BeforeTempWrite,
    AfterTempFlush,
    AfterTempVerify,
    BeforeAtomicReplace,
    AfterAtomicReplace,
    AfterJournalLength,
    AfterJournalFrame,
    AfterJournalFlush
}

public interface IAiFileStateStoreFaultInjector
{
    void Inject(AiFileStateStoreFaultPoint point);
}

/// <summary>
/// Lokaler Phase-9-State-Store. Er liegt außerhalb von Projektverzeichnissen,
/// verwendet einen exklusiven Writer-Lock und ersetzt Manifest/Snapshot atomar.
/// </summary>
public sealed class AiLocalFileRuntimeStateStore : IAiRuntimeStateStore, IAiRuntimeStateMaintenanceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _rootPath;
    private readonly AiRuntimePersistenceOptions _options;
    private readonly IAiFileStateStoreFaultInjector? _faults;

    public string StoreId { get; }
    public string RootPath => _rootPath;

    public AiLocalFileRuntimeStateStore(
        string rootPath,
        string storeId,
        AiRuntimePersistenceOptions? options = null,
        IAiFileStateStoreFaultInjector? faultInjector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateStoreId(storeId);
        _rootPath = Path.GetFullPath(rootPath);
        RejectGitWorkspace(_rootPath);
        StoreId = storeId;
        _options = CloneOptions(options ?? new AiRuntimePersistenceOptions());
        ValidateOptions(_options);
        _faults = faultInjector;
    }

    public static string GetDefaultRootPath(string storeId)
    {
        ValidateStoreId(storeId);
        string basePath;
        if (OperatingSystem.IsMacOS())
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "AAIAModuleManager");
        }
        else if (OperatingSystem.IsLinux())
        {
            basePath = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
            basePath = Path.Combine(basePath, "AAIAModuleManager");
        }
        else
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AAIAModuleManager");
        }
        return Path.Combine(basePath, "air-state", storeId);
    }

    public ValueTask<IAiRuntimeStateStoreSession> OpenAsync(
        AiStateStoreOpenMode mode,
        string runtimeInstanceId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == AiStateStoreOpenMode.ReadWrite && !_options.Enabled)
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.Disabled,
                "Lokale AIR-Persistenz ist deaktiviert.");

        if (mode == AiStateStoreOpenMode.ReadWrite)
        {
            Directory.CreateDirectory(_rootPath);
            SecureDirectory(_rootPath);
        }
        else if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException("State Store existiert nicht.");
        }

        var lockPath = Path.Combine(_rootPath, "writer.lock");
        FileStream? lockStream = null;
        try
        {
            lockStream = mode == AiStateStoreOpenMode.ReadWrite
                ? new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
                    1, FileOptions.WriteThrough)
                : new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (mode == AiStateStoreOpenMode.ReadWrite) SecureFile(lockPath);

            var session = new Session(
                _rootPath, StoreId, runtimeInstanceId, mode, lockStream, _options, _faults);
            lockStream = null;
            return ValueTask.FromResult<IAiRuntimeStateStoreSession>(session);
        }
        catch (IOException ex) when (mode == AiStateStoreOpenMode.ReadWrite)
        {
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.StoreLocked,
                "State Store besitzt bereits einen Writer.", ex);
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    public async ValueTask<AiRuntimeStateDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_rootPath))
            return new AiRuntimeStateDiagnostics
            {
                StoreId = StoreId,
                Status = AiRuntimeRecoveryStatus.Disabled,
                ReasonCode = AiRuntimeStateReasonCodes.Disabled,
                RedactedMessage = "State Store existiert noch nicht."
            };

        var quarantinePath = Path.Combine(_rootPath, "quarantine.json");
        if (File.Exists(quarantinePath))
        {
            var record = ReadQuarantineRecord(quarantinePath);
            return new AiRuntimeStateDiagnostics
            {
                StoreId = StoreId,
                Status = AiRuntimeRecoveryStatus.Quarantined,
                LastSequence = record?.LastSequence ?? 0,
                StoreSizeBytes = StoreSizeForDiagnostics(),
                LastUpdatedAtUtc = record?.QuarantinedAtUtc,
                ReasonCode = AiRuntimeStateReasonCodes.Quarantined,
                RedactedMessage = AiAuditService.Redact(record?.Reason ?? "quarantine_record_unreadable")
            };
        }

        await using var session = await OpenAsync(AiStateStoreOpenMode.ReadOnly, "diagnostics", ct)
            .ConfigureAwait(false);
        var manifest = await session.LoadManifestAsync(ct).ConfigureAwait(false);
        var snapshot = await session.LoadSnapshotAsync(ct).ConfigureAwait(false);
        return new AiRuntimeStateDiagnostics
        {
            StoreId = StoreId,
            Status = AiRuntimeRecoveryStatus.Ready,
            SchemaVersion = manifest?.SchemaVersion ?? snapshot?.SchemaVersion,
            LastSequence = manifest?.LastSequence ?? snapshot?.Sequence ?? 0,
            SnapshotSequence = snapshot?.Sequence ?? 0,
            StoreSizeBytes = StoreSizeForDiagnostics(),
            LastUpdatedAtUtc = manifest?.UpdatedAtUtc ?? snapshot?.CreatedAtUtc
        };
    }

    public ValueTask<AiStateStoreBackupResult> CreateBackupAsync(
        string runtimeInstanceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_rootPath))
            throw new DirectoryNotFoundException("State Store existiert nicht.");

        using var maintenanceLock = AcquireMaintenanceLock();
        var createdAt = DateTime.UtcNow;
        var backupId = $"backup-{createdAt:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var backupRoot = Path.Combine(_rootPath, "backups");
        var target = Path.Combine(backupRoot, backupId);
        var temp = target + ".tmp";
        Directory.CreateDirectory(backupRoot);
        SecureDirectory(backupRoot);
        Directory.CreateDirectory(temp);
        SecureDirectory(temp);
        try
        {
            foreach (var name in new[] { "manifest.json", "snapshot.bin", "journal.bin", "quarantine.json" })
            {
                var source = Path.Combine(_rootPath, name);
                if (!File.Exists(source)) continue;
                var destination = Path.Combine(temp, name);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough);
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
                SecureFile(destination);
            }
            var throughSequence = ReadLastSequenceBestEffort();
            var metadata = new BackupRecord
            {
                BackupId = backupId,
                CreatedAtUtc = createdAt,
                ThroughSequence = throughSequence
            };
            var metadataPath = Path.Combine(temp, "backup.json");
            File.WriteAllBytes(metadataPath, JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions));
            SecureFile(metadataPath);
            Directory.Move(temp, target);
            return ValueTask.FromResult(new AiStateStoreBackupResult
            {
                BackupId = backupId,
                CreatedAtUtc = createdAt,
                ThroughSequence = throughSequence
            });
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch { /* nächste Wartung kann verwaiste Temp-Verzeichnisse entfernen */ }
        }
    }

    public async ValueTask<AiStateStoreRepairResult> RepairAsync(
        string runtimeInstanceId,
        string backupId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        ValidateBackupId(backupId);
        ct.ThrowIfCancellationRequested();
        if (!_options.Enabled)
            throw new AiStateStoreException(AiRuntimeStateReasonCodes.Disabled,
                "Lokale AIR-Persistenz ist deaktiviert.");
        var backupPath = Path.Combine(_rootPath, "backups", backupId);
        if (!Directory.Exists(backupPath) || !File.Exists(Path.Combine(backupPath, "backup.json")))
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.BackupMissing,
                "Verpflichtendes Backup wurde nicht gefunden.");
        var quarantinePath = Path.Combine(_rootPath, "quarantine.json");
        if (!File.Exists(quarantinePath))
            return new AiStateStoreRepairResult
            {
                Repaired = false,
                BackupId = backupId,
                ReasonCode = AiRuntimeStateReasonCodes.RepairNotRequired
            };

        FileStream? maintenanceLock = AcquireMaintenanceLock();
        try
        {
            await using var session = new Session(
                _rootPath, StoreId, runtimeInstanceId, AiStateStoreOpenMode.ReadWrite,
                maintenanceLock, _options, _faults, allowQuarantinedMaintenance: true);
            maintenanceLock = null;
            session.CompleteRepair();
            return new AiStateStoreRepairResult
            {
                Repaired = true,
                BackupId = backupId
            };
        }
        finally
        {
            maintenanceLock?.Dispose();
        }
    }

    private sealed class Session : IAiRuntimeStateStoreSession
    {
        private readonly object _gate = new();
        private readonly string _root;
        private readonly string _manifestPath;
        private readonly string _snapshotPath;
        private readonly string _journalPath;
        private readonly string _quarantinePath;
        private readonly FileStream _lockStream;
        private readonly AiRuntimePersistenceOptions _options;
        private readonly IAiFileStateStoreFaultInjector? _faults;
        private bool _disposed;
        private long _lastSequence;

        public string StoreId { get; }
        public string RuntimeInstanceId { get; }
        public AiStateStoreOpenMode Mode { get; }

        public bool IsQuarantined
        {
            get { lock (_gate) return File.Exists(_quarantinePath); }
        }

        public string? QuarantineReason
        {
            get
            {
                lock (_gate)
                {
                    if (!File.Exists(_quarantinePath)) return null;
                    try
                    {
                        var record = JsonSerializer.Deserialize<QuarantineRecord>(
                            File.ReadAllBytes(_quarantinePath), JsonOptions);
                        return record?.Reason;
                    }
                    catch { return "quarantine_record_unreadable"; }
                }
            }
        }

        public Session(
            string root,
            string storeId,
            string runtimeInstanceId,
            AiStateStoreOpenMode mode,
            FileStream lockStream,
            AiRuntimePersistenceOptions options,
            IAiFileStateStoreFaultInjector? faults,
            bool allowQuarantinedMaintenance = false)
        {
            _root = root;
            StoreId = storeId;
            RuntimeInstanceId = runtimeInstanceId;
            Mode = mode;
            _lockStream = lockStream;
            _options = options;
            _faults = faults;
            _manifestPath = Path.Combine(root, "manifest.json");
            _snapshotPath = Path.Combine(root, "snapshot.bin");
            _journalPath = Path.Combine(root, "journal.bin");
            _quarantinePath = Path.Combine(root, "quarantine.json");

            if (mode == AiStateStoreOpenMode.ReadWrite)
            {
                if (File.Exists(_quarantinePath) && !allowQuarantinedMaintenance)
                    throw new AiStateStoreException(
                        AiRuntimeStateReasonCodes.Quarantined, "State Store ist quarantänisiert.");
                CleanupStaleTemps();
                RecoverCrashTail();
            }
            _lastSequence = DetermineLastSequence(allowCrashTail: mode == AiStateStoreOpenMode.ReadOnly);
        }

        public void CompleteRepair()
        {
            lock (_gate)
            {
                CheckUsable(CancellationToken.None);
                if (!File.Exists(_quarantinePath)) return;
                // Der Konstruktor hat Snapshot, Journal und Sequenzen bereits fail-closed validiert
                // und ausschließlich einen sicheren Crash-Tail normalisiert.
                File.Delete(_quarantinePath);
            }
        }

        public ValueTask<AiRuntimeStateManifest?> LoadManifestAsync(CancellationToken ct = default)
        {
            lock (_gate)
            {
                CheckUsable(ct);
                if (!File.Exists(_manifestPath)) return ValueTask.FromResult<AiRuntimeStateManifest?>(null);
                EnsureReadableSize(_manifestPath);
                return ValueTask.FromResult<AiRuntimeStateManifest?>(ReadManifestBytes(File.ReadAllBytes(_manifestPath)));
            }
        }

        public ValueTask WriteManifestAsync(AiRuntimeStateManifest manifest, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            lock (_gate)
            {
                CheckWritable(ct);
                ValidateManifest(manifest);
                if (!string.Equals(manifest.RuntimeInstanceId, RuntimeInstanceId, StringComparison.Ordinal))
                    throw new ArgumentException("Manifest gehört einer anderen Runtime-Instanz.", nameof(manifest));
                if (manifest.LastSequence != _lastSequence)
                    throw new AiStateStoreException(
                        AiRuntimeStateReasonCodes.JournalGap,
                        "Manifest-Sequenz entspricht nicht der letzten Store-Sequenz.");
                var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                AtomicWrite(_manifestPath, bytes, VerifyManifestBytes);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<AiRuntimeStateSnapshot?> LoadSnapshotAsync(CancellationToken ct = default)
        {
            lock (_gate)
            {
                CheckUsable(ct);
                if (!File.Exists(_snapshotPath)) return ValueTask.FromResult<AiRuntimeStateSnapshot?>(null);
                EnsureReadableSize(_snapshotPath);
                var frame = File.ReadAllBytes(_snapshotPath);
                return ValueTask.FromResult<AiRuntimeStateSnapshot?>(
                    AiRuntimeStateCodec.DeserializeSnapshot(
                        frame, _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes()));
            }
        }

        public ValueTask WriteSnapshotAsync(AiRuntimeStateSnapshot snapshot, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            lock (_gate)
            {
                CheckWritable(ct);
                if (snapshot.Sequence > _lastSequence)
                    throw new AiStateStoreException(
                        AiRuntimeStateReasonCodes.JournalGap,
                        "Snapshot liegt hinter der letzten Store-Sequenz.");
                var frame = AiRuntimeStateCodec.SerializeSnapshot(
                    snapshot, _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes());
                AtomicWrite(_snapshotPath, frame, bytes =>
                    AiRuntimeStateCodec.DeserializeSnapshot(
                        bytes, _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes()));
            }
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<AiRuntimeJournalEntry> ReadJournalAsync(
            long afterSequence,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
            AiRuntimeJournalEntry[] entries;
            lock (_gate)
            {
                CheckUsable(ct);
                entries = ReadJournalFile(allowCrashTail: true).Entries
                    .Where(entry => entry.Sequence > afterSequence)
                    .ToArray();
            }
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                yield return entry;
                await Task.Yield();
            }
        }

        public ValueTask AppendJournalAsync(AiRuntimeJournalEntry entry, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (_gate)
            {
                CheckWritable(ct);
                var expected = _lastSequence + 1;
                if (entry.Sequence != expected)
                    throw new AiStateStoreException(
                        AiRuntimeStateReasonCodes.JournalGap,
                        $"Journal-Sequenz {entry.Sequence} ist ungültig; erwartet wird {expected}.");
                var current = ReadJournalFile(allowCrashTail: false);
                if (current.Entries.Any(existing => existing.OperationId == entry.OperationId))
                    throw new InvalidOperationException("OperationId ist bereits vorhanden.");
                var frame = AiRuntimeStateCodec.SerializeJournalEntry(
                    entry, _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes());
                EnsureQuota(frame.LongLength + sizeof(int), replacingPath: null);

                try
                {
                    using var stream = new FileStream(
                        _journalPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
                        4096, FileOptions.WriteThrough);
                    stream.Seek(0, SeekOrigin.End);
                    Span<byte> length = stackalloc byte[sizeof(int)];
                    BinaryPrimitives.WriteInt32BigEndian(length, frame.Length);
                    stream.Write(length);
                    _faults?.Inject(AiFileStateStoreFaultPoint.AfterJournalLength);
                    stream.Write(frame);
                    _faults?.Inject(AiFileStateStoreFaultPoint.AfterJournalFrame);
                    stream.Flush(flushToDisk: true);
                    _faults?.Inject(AiFileStateStoreFaultPoint.AfterJournalFlush);
                    SecureFile(_journalPath);
                    _lastSequence = entry.Sequence;
                }
                catch
                {
                    _lastSequence = DetermineLastSequence(allowCrashTail: true);
                    throw;
                }
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken ct = default)
        {
            lock (_gate)
            {
                CheckWritable(ct);
                if (File.Exists(_journalPath))
                {
                    using var stream = new FileStream(
                        _journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                        1, FileOptions.WriteThrough);
                    stream.Flush(flushToDisk: true);
                }
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask CompactAsync(long throughSequence, CancellationToken ct = default)
        {
            if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
            lock (_gate)
            {
                CheckWritable(ct);
                var snapshot = File.Exists(_snapshotPath)
                    ? AiRuntimeStateCodec.DeserializeSnapshot(
                        File.ReadAllBytes(_snapshotPath), _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes())
                    : null;
                if (snapshot is null || snapshot.Sequence < throughSequence)
                    throw new InvalidOperationException(
                        "Journal darf nur bis zu einem bestätigten Snapshot kompaktiert werden.");
                var remaining = ReadJournalFile(allowCrashTail: false).Entries
                    .Where(entry => entry.Sequence > throughSequence)
                    .ToArray();
                var bytes = BuildJournalBytes(remaining);
                AtomicWrite(_journalPath, bytes, data => ParseJournalBytes(data, allowCrashTail: false));
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask QuarantineAsync(string reason, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            lock (_gate)
            {
                CheckWritable(ct);
                var record = new QuarantineRecord
                {
                    Reason = SanitizeReason(reason),
                    QuarantinedAtUtc = DateTime.UtcNow,
                    LastSequence = _lastSequence
                };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
                AtomicWrite(_quarantinePath, bytes, data =>
                    JsonSerializer.Deserialize<QuarantineRecord>(data, JsonOptions)
                    ?? throw new FormatException("Quarantäne-Eintrag ist ungültig."));
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposed) return ValueTask.CompletedTask;
                _lockStream.Dispose();
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }

        private void AtomicWrite<T>(string targetPath, byte[] bytes, Func<byte[], T> verify)
        {
            CheckQuotaForReplacement(targetPath, bytes.LongLength);
            var tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            _faults?.Inject(AiFileStateStoreFaultPoint.BeforeTempWrite);
            try
            {
                using (var stream = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                SecureFile(tempPath);
                _faults?.Inject(AiFileStateStoreFaultPoint.AfterTempFlush);
                verify(File.ReadAllBytes(tempPath));
                _faults?.Inject(AiFileStateStoreFaultPoint.AfterTempVerify);
                _faults?.Inject(AiFileStateStoreFaultPoint.BeforeAtomicReplace);
                File.Move(tempPath, targetPath, overwrite: true);
                _faults?.Inject(AiFileStateStoreFaultPoint.AfterAtomicReplace);
                verify(File.ReadAllBytes(targetPath));
                SecureFile(targetPath);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch { /* stale temp is removed by the next writer */ }
            }
        }

        private JournalReadResult ReadJournalFile(bool allowCrashTail)
        {
            if (!File.Exists(_journalPath)) return new JournalReadResult(Array.Empty<AiRuntimeJournalEntry>(), 0, false);
            EnsureReadableSize(_journalPath);
            return ParseJournalBytes(File.ReadAllBytes(_journalPath), allowCrashTail);
        }

        private JournalReadResult ParseJournalBytes(byte[] bytes, bool allowCrashTail)
        {
            var entries = new List<AiRuntimeJournalEntry>();
            var offset = 0;
            long? previous = null;
            while (offset < bytes.Length)
            {
                var recordStart = offset;
                if (bytes.Length - offset < sizeof(int))
                    return TailOrThrow(entries, recordStart, allowCrashTail);
                var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, sizeof(int)));
                offset += sizeof(int);
                if (length <= 0 || length > CheckedMaxRecordBytes())
                    throw Corrupt(AiRuntimeStateReasonCodes.JournalCorrupt, "Journal-Record-Länge ist ungültig.");
                if (bytes.Length - offset < length)
                    return TailOrThrow(entries, recordStart, allowCrashTail);
                var entry = AiRuntimeStateCodec.DeserializeJournalEntry(
                    bytes.AsSpan(offset, length), null,
                    _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes());
                if (previous.HasValue && entry.Sequence != previous.Value + 1)
                    throw Corrupt(AiRuntimeStateReasonCodes.JournalGap, "Journal enthält eine Sequenzlücke.");
                entries.Add(entry);
                previous = entry.Sequence;
                offset += length;
            }
            return new JournalReadResult(entries.ToArray(), offset, false);
        }

        private static JournalReadResult TailOrThrow(
            List<AiRuntimeJournalEntry> entries, int validLength, bool allowCrashTail)
        {
            if (!allowCrashTail)
                throw Corrupt(AiRuntimeStateReasonCodes.JournalCorrupt, "Journal endet mit unvollständigem Record.");
            return new JournalReadResult(entries.ToArray(), validLength, true);
        }

        private byte[] BuildJournalBytes(IEnumerable<AiRuntimeJournalEntry> entries)
        {
            using var stream = new MemoryStream();
            var length = new byte[sizeof(int)];
            foreach (var entry in entries)
            {
                var frame = AiRuntimeStateCodec.SerializeJournalEntry(
                    entry, _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes());
                BinaryPrimitives.WriteInt32BigEndian(length, frame.Length);
                stream.Write(length);
                stream.Write(frame);
            }
            return stream.ToArray();
        }

        private void RecoverCrashTail()
        {
            if (!File.Exists(_journalPath)) return;
            var bytes = File.ReadAllBytes(_journalPath);
            var result = ParseJournalBytes(bytes, allowCrashTail: true);
            if (!result.HasCrashTail) return;

            var quarantineDir = Path.Combine(_root, "quarantine");
            Directory.CreateDirectory(quarantineDir);
            SecureDirectory(quarantineDir);
            var tail = bytes.AsSpan(result.ValidLength).ToArray();
            var tailPath = Path.Combine(quarantineDir, $"journal-tail-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(tailPath, tail);
            SecureFile(tailPath);
            using var stream = new FileStream(
                _journalPath, FileMode.Open, FileAccess.Write, FileShare.Read,
                1, FileOptions.WriteThrough);
            stream.SetLength(result.ValidLength);
            stream.Flush(flushToDisk: true);
        }

        private long DetermineLastSequence(bool allowCrashTail)
        {
            AiRuntimeStateManifest? manifest = null;
            if (File.Exists(_manifestPath))
                manifest = ReadManifestBytes(File.ReadAllBytes(_manifestPath));

            AiRuntimeStateSnapshot? snapshot = null;
            if (File.Exists(_snapshotPath))
                snapshot = AiRuntimeStateCodec.DeserializeSnapshot(
                    File.ReadAllBytes(_snapshotPath), _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes());

            var journal = ReadJournalFile(allowCrashTail);
            var snapshotSequence = snapshot?.Sequence ?? 0;
            var firstAfterSnapshot = journal.Entries.FirstOrDefault(entry => entry.Sequence > snapshotSequence);
            if (firstAfterSnapshot is not null && firstAfterSnapshot.Sequence != snapshotSequence + 1)
                throw Corrupt(AiRuntimeStateReasonCodes.JournalGap,
                    "Journal enthält nach dem Snapshot eine Sequenzlücke.");
            var physicalLastSequence = Math.Max(
                snapshotSequence,
                journal.Entries.Count > 0 ? journal.Entries[^1].Sequence : 0);
            if (manifest is not null && manifest.LastSequence > physicalLastSequence)
                throw Corrupt(AiRuntimeStateReasonCodes.JournalGap,
                    "Manifest verweist auf nicht vorhandene Journal-Sequenzen.");
            return physicalLastSequence;
        }

        private void CleanupStaleTemps()
        {
            foreach (var path in Directory.EnumerateFiles(_root, "*.tmp-*", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(path); }
                catch { /* späterer Open-Versuch kann erneut bereinigen */ }
            }
        }

        private void ValidateManifest(AiRuntimeStateManifest manifest)
        {
            if (!AiRuntimeStateSchema.IsSupported(manifest.SchemaVersion))
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.SchemaUnsupported,
                    $"Schema-Version {manifest.SchemaVersion} wird nicht unterstützt.");
            if (!string.Equals(manifest.StoreId, StoreId, StringComparison.Ordinal))
                throw new ArgumentException("Manifest gehört zu einem anderen Store.", nameof(manifest));
            if (manifest.LastSequence < 0 || manifest.SnapshotSequence < 0 ||
                manifest.SnapshotSequence > manifest.LastSequence)
                throw new ArgumentOutOfRangeException(nameof(manifest));
            if (manifest.CreatedAtUtc.Kind != DateTimeKind.Utc || manifest.UpdatedAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Manifest-Zeitpunkte müssen UTC sein.", nameof(manifest));
            if (manifest.FeatureFlags is null)
                throw new ArgumentException("FeatureFlags fehlen.", nameof(manifest));
            if (manifest.SnapshotSequence > 0 || manifest.SnapshotChecksumSha256 is not null)
            {
                if (!File.Exists(_snapshotPath))
                    throw Corrupt(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                        "Manifest referenziert einen fehlenden Snapshot.");
                var snapshot = AiRuntimeStateCodec.DeserializeSnapshot(
                    File.ReadAllBytes(_snapshotPath), _options.MaxProtectedPayloadBytes, CheckedMaxRecordBytes());
                if (snapshot.Sequence != manifest.SnapshotSequence ||
                    !string.Equals(snapshot.ChecksumSha256, manifest.SnapshotChecksumSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw Corrupt(AiRuntimeStateReasonCodes.SnapshotCorrupt,
                        "Manifest und Snapshot stimmen nicht überein.");
            }
        }

        private AiRuntimeStateManifest VerifyManifestBytes(byte[] bytes)
        {
            return ReadManifestBytes(bytes);
        }

        private AiRuntimeStateManifest ReadManifestBytes(byte[] bytes)
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<AiRuntimeStateManifest>(bytes, JsonOptions)
                    ?? throw new JsonException("Manifest ist leer.");
                ValidateManifest(manifest);
                return manifest;
            }
            catch (AiStateStoreException) { throw; }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.SnapshotCorrupt,
                    "Manifest ist beschädigt.", ex);
            }
        }

        private void EnsureReadableSize(string path)
        {
            if (new FileInfo(path).Length > _options.MaxStoreBytes)
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.QuotaExceeded, "State-Datei überschreitet die Store-Quota.");
        }

        private void EnsureQuota(long additionalBytes, string? replacingPath)
        {
            var current = StoreSize(replacingPath);
            if (additionalBytes < 0 || current > _options.MaxStoreBytes - additionalBytes)
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.QuotaExceeded, "State-Store-Quota würde überschritten.");
        }

        private void CheckQuotaForReplacement(string targetPath, long replacementBytes)
            => EnsureQuota(replacementBytes, targetPath);

        private long StoreSize(string? excludingPath)
        {
            long total = 0;
            foreach (var path in new[] { _manifestPath, _snapshotPath, _journalPath, _quarantinePath })
            {
                if (string.Equals(path, excludingPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                total = checked(total + new FileInfo(path).Length);
            }
            return total;
        }

        private int CheckedMaxRecordBytes()
            => checked(_options.MaxProtectedPayloadBytes + 4096);

        private void CheckUsable(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private void CheckWritable(CancellationToken ct)
        {
            CheckUsable(ct);
            if (Mode != AiStateStoreOpenMode.ReadWrite)
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ReadOnly, "State Store wurde read-only geöffnet.");
            if (File.Exists(_quarantinePath))
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.Quarantined, "State Store ist quarantänisiert.");
        }

        private sealed record JournalReadResult(
            IReadOnlyList<AiRuntimeJournalEntry> Entries,
            int ValidLength,
            bool HasCrashTail);

        private sealed class QuarantineRecord
        {
            public string Reason { get; init; } = "";
            public DateTime QuarantinedAtUtc { get; init; }
            public long LastSequence { get; init; }
        }
    }

    private FileStream AcquireMaintenanceLock()
    {
        Directory.CreateDirectory(_rootPath);
        SecureDirectory(_rootPath);
        var lockPath = Path.Combine(_rootPath, "writer.lock");
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.WriteThrough);
            SecureFile(lockPath);
            return stream;
        }
        catch (IOException ex)
        {
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.StoreLocked,
                "State Store besitzt bereits einen aktiven Zugriff.", ex);
        }
    }

    private long StoreSizeForDiagnostics()
    {
        if (!Directory.Exists(_rootPath)) return 0;
        return new[] { "manifest.json", "snapshot.bin", "journal.bin", "quarantine.json" }
            .Select(name => Path.Combine(_rootPath, name))
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
    }

    private long ReadLastSequenceBestEffort()
    {
        var manifestPath = Path.Combine(_rootPath, "manifest.json");
        if (!File.Exists(manifestPath)) return 0;
        try
        {
            return JsonSerializer.Deserialize<AiRuntimeStateManifest>(
                File.ReadAllBytes(manifestPath), JsonOptions)?.LastSequence ?? 0;
        }
        catch { return 0; }
    }

    private static MaintenanceQuarantineRecord? ReadQuarantineRecord(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MaintenanceQuarantineRecord>(
                File.ReadAllBytes(path), JsonOptions);
        }
        catch { return null; }
    }

    private static void ValidateBackupId(string backupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        if (!backupId.StartsWith("backup-", StringComparison.Ordinal) || backupId.Length > 100 ||
            backupId.Any(character => !(char.IsLetterOrDigit(character) || character == '-')))
            throw new ArgumentException("Backup-ID ist ungültig.", nameof(backupId));
    }

    private sealed class BackupRecord
    {
        public required string BackupId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public long ThroughSequence { get; init; }
    }

    private sealed class MaintenanceQuarantineRecord
    {
        public string Reason { get; init; } = "";
        public DateTime QuarantinedAtUtc { get; init; }
        public long LastSequence { get; init; }
    }

    private static void ValidateStoreId(string storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        if (storeId.Length > 100 || storeId.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException("StoreId enthält unzulässige Zeichen.", nameof(storeId));
    }

    private static void ValidateOptions(AiRuntimePersistenceOptions options)
    {
        if (options.MaxStoreBytes <= 0 || options.MaxProtectedPayloadBytes <= 0 ||
            options.MaxProtectedPayloadBytes > options.MaxStoreBytes)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    private static AiRuntimePersistenceOptions CloneOptions(AiRuntimePersistenceOptions options) => new()
    {
        Enabled = options.Enabled,
        MaxStoreBytes = options.MaxStoreBytes,
        MaxProtectedPayloadBytes = options.MaxProtectedPayloadBytes,
        SnapshotJournalEntryThreshold = options.SnapshotJournalEntryThreshold,
        SnapshotInterval = options.SnapshotInterval,
        IdempotencyTtl = options.IdempotencyTtl,
        MaxIdempotencyEntries = options.MaxIdempotencyEntries
    };

    private static string SanitizeReason(string reason)
    {
        if (AiMessageSafetyPolicy.ContainsSensitiveContent(reason))
            return "redacted_sensitive_reason";
        return reason.Length <= 500 ? reason : reason[..500];
    }

    private static void RejectGitWorkspace(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                throw new ArgumentException("State Store darf nicht in einem Git-Workspace liegen.", nameof(path));
            current = current.Parent;
        }
    }

    private static void SecureDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void SecureFile(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static AiStateStoreException Corrupt(string code, string message)
        => new(code, message);
}
