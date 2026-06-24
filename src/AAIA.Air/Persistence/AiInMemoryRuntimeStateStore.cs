using System.Runtime.CompilerServices;

namespace AAIA.Air.Persistence;

/// <summary>Gemeinsamer, isolierbarer Speicher für mehrere In-Memory-Store-Instanzen.</summary>
public sealed class AiInMemoryRuntimeStateStoreBackend
{
    internal object Gate { get; } = new();
    internal AiRuntimeStateManifest? Manifest { get; set; }
    internal AiRuntimeStateSnapshot? Snapshot { get; set; }
    internal List<AiRuntimeJournalEntry> Journal { get; } = new();
    internal HashSet<string> OperationIds { get; } = new(StringComparer.Ordinal);
    internal string? WriterToken { get; set; }
    internal long LastSequence { get; set; }
    internal long LastFlushedSequenceValue { get; set; }
    internal bool Quarantined { get; set; }
    internal string? QuarantineReasonValue { get; set; }

    public string StoreId { get; }

    public AiInMemoryRuntimeStateStoreBackend(string? storeId = null)
    {
        StoreId = string.IsNullOrWhiteSpace(storeId)
            ? Guid.NewGuid().ToString("N")
            : storeId;
    }

    public long LastFlushedSequence
    {
        get { lock (Gate) return LastFlushedSequenceValue; }
    }

    public int JournalCount
    {
        get { lock (Gate) return Journal.Count; }
    }
}

/// <summary>
/// Thread-sicherer Referenz-Store für Tests und frühe Composition. Er bildet
/// Single-Writer, defensive Kopien, Sequenzen, Quarantäne und Quoten ab, schreibt
/// aber ausdrücklich keine Dateien.
/// </summary>
public sealed class AiInMemoryRuntimeStateStore : IAiRuntimeStateStore
{
    private readonly AiInMemoryRuntimeStateStoreBackend _backend;
    private readonly long _maxStoreBytes;

    public string StoreId => _backend.StoreId;

    public AiInMemoryRuntimeStateStore(
        AiInMemoryRuntimeStateStoreBackend backend,
        long maxStoreBytes = 100L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (maxStoreBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxStoreBytes));
        _backend = backend;
        _maxStoreBytes = maxStoreBytes;
    }

    public ValueTask<IAiRuntimeStateStoreSession> OpenAsync(
        AiStateStoreOpenMode mode,
        string runtimeInstanceId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        lock (_backend.Gate)
        {
            if (mode == AiStateStoreOpenMode.ReadWrite)
            {
                if (_backend.Quarantined)
                    throw Error(AiRuntimeStateReasonCodes.Quarantined, "State Store ist quarantänisiert.");
                if (_backend.WriterToken is not null)
                    throw Error(AiRuntimeStateReasonCodes.StoreLocked, "State Store besitzt bereits einen Writer.");
            }

            var writerToken = mode == AiStateStoreOpenMode.ReadWrite
                ? Guid.NewGuid().ToString("N")
                : null;
            if (writerToken is not null) _backend.WriterToken = writerToken;
            return ValueTask.FromResult<IAiRuntimeStateStoreSession>(
                new Session(_backend, _maxStoreBytes, runtimeInstanceId, mode, writerToken));
        }
    }

    private sealed class Session : IAiRuntimeStateStoreSession
    {
        private readonly AiInMemoryRuntimeStateStoreBackend _backend;
        private readonly long _maxStoreBytes;
        private readonly string? _writerToken;
        private bool _disposed;

        public string StoreId => _backend.StoreId;
        public string RuntimeInstanceId { get; }
        public AiStateStoreOpenMode Mode { get; }

        public bool IsQuarantined
        {
            get { lock (_backend.Gate) return _backend.Quarantined; }
        }

        public string? QuarantineReason
        {
            get { lock (_backend.Gate) return _backend.QuarantineReasonValue; }
        }

        public Session(
            AiInMemoryRuntimeStateStoreBackend backend,
            long maxStoreBytes,
            string runtimeInstanceId,
            AiStateStoreOpenMode mode,
            string? writerToken)
        {
            _backend = backend;
            _maxStoreBytes = maxStoreBytes;
            RuntimeInstanceId = runtimeInstanceId;
            Mode = mode;
            _writerToken = writerToken;
        }

        public ValueTask<AiRuntimeStateManifest?> LoadManifestAsync(CancellationToken ct = default)
        {
            CheckUsable(ct);
            lock (_backend.Gate)
                return ValueTask.FromResult(_backend.Manifest is null ? null : Clone(_backend.Manifest));
        }

        public ValueTask WriteManifestAsync(AiRuntimeStateManifest manifest, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            CheckWritable(ct);
            ValidateSchema(manifest.SchemaVersion);
            if (!string.Equals(manifest.StoreId, StoreId, StringComparison.Ordinal) ||
                !string.Equals(manifest.RuntimeInstanceId, RuntimeInstanceId, StringComparison.Ordinal))
                throw new ArgumentException("Manifest gehört nicht zu Store und Runtime-Instanz.", nameof(manifest));
            if (manifest.LastSequence < 0 || manifest.SnapshotSequence < 0 ||
                manifest.SnapshotSequence > manifest.LastSequence)
                throw new ArgumentOutOfRangeException(nameof(manifest));
            ValidateUtc(manifest.CreatedAtUtc, nameof(manifest.CreatedAtUtc));
            ValidateUtc(manifest.UpdatedAtUtc, nameof(manifest.UpdatedAtUtc));
            ValidateOptionalChecksum(manifest.SnapshotChecksumSha256, nameof(manifest));
            if (manifest.FeatureFlags is null) throw new ArgumentException("FeatureFlags fehlen.", nameof(manifest));

            lock (_backend.Gate)
            {
                CheckWritableLocked();
                if (manifest.LastSequence != _backend.LastSequence)
                    throw Error(AiRuntimeStateReasonCodes.JournalGap,
                        "Manifest-Sequenz entspricht nicht der letzten Store-Sequenz.");
                _backend.Manifest = Clone(manifest);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<AiRuntimeStateSnapshot?> LoadSnapshotAsync(CancellationToken ct = default)
        {
            CheckUsable(ct);
            lock (_backend.Gate)
                return ValueTask.FromResult(_backend.Snapshot is null ? null : Clone(_backend.Snapshot));
        }

        public ValueTask WriteSnapshotAsync(AiRuntimeStateSnapshot snapshot, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            CheckWritable(ct);
            ValidateSchema(snapshot.SchemaVersion);
            if (snapshot.Sequence < 0) throw new ArgumentOutOfRangeException(nameof(snapshot));
            ValidateUtc(snapshot.CreatedAtUtc, nameof(snapshot.CreatedAtUtc));
            ValidatePayload(snapshot.Payload, nameof(snapshot));
            ValidateChecksum(snapshot.ChecksumSha256, nameof(snapshot));

            lock (_backend.Gate)
            {
                CheckWritableLocked();
                if (snapshot.Sequence > _backend.LastSequence)
                    throw Error(AiRuntimeStateReasonCodes.JournalGap,
                        "Snapshot liegt hinter der letzten Store-Sequenz.");
                if (_backend.Snapshot is not null && snapshot.Sequence < _backend.Snapshot.Sequence)
                    throw new InvalidOperationException("Snapshot-Sequenz darf nicht zurückgehen.");
                EnsureQuotaLocked(snapshot.Payload.LongLength, replacingSnapshot: true);
                _backend.Snapshot = Clone(snapshot);
            }
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<AiRuntimeJournalEntry> ReadJournalAsync(
            long afterSequence,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            CheckUsable(ct);
            if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
            AiRuntimeJournalEntry[] entries;
            lock (_backend.Gate)
                entries = _backend.Journal
                    .Where(entry => entry.Sequence > afterSequence)
                    .OrderBy(entry => entry.Sequence)
                    .Select(Clone)
                    .ToArray();
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
            CheckWritable(ct);
            ValidateSchema(entry.SchemaVersion);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.OperationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.EventType);
            ValidateUtc(entry.OccurredAtUtc, nameof(entry.OccurredAtUtc));
            ValidatePayload(entry.Payload, nameof(entry));
            ValidateChecksum(entry.ChecksumSha256, nameof(entry));

            lock (_backend.Gate)
            {
                CheckWritableLocked();
                var expected = _backend.LastSequence + 1;
                if (entry.Sequence != expected)
                    throw Error(AiRuntimeStateReasonCodes.JournalGap,
                        $"Journal-Sequenz {entry.Sequence} ist ungültig; erwartet wird {expected}.");
                if (_backend.OperationIds.Contains(entry.OperationId))
                    throw new InvalidOperationException("OperationId ist bereits vorhanden.");
                EnsureQuotaLocked(entry.Payload.LongLength, replacingSnapshot: false);
                _backend.OperationIds.Add(entry.OperationId);
                _backend.Journal.Add(Clone(entry));
                _backend.LastSequence = entry.Sequence;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken ct = default)
        {
            CheckWritable(ct);
            lock (_backend.Gate)
            {
                CheckWritableLocked();
                _backend.LastFlushedSequenceValue = _backend.LastSequence;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask CompactAsync(long throughSequence, CancellationToken ct = default)
        {
            CheckWritable(ct);
            if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
            lock (_backend.Gate)
            {
                CheckWritableLocked();
                if (_backend.Snapshot is null || _backend.Snapshot.Sequence < throughSequence)
                    throw new InvalidOperationException("Journal darf nur bis zu einem bestätigten Snapshot kompaktiert werden.");
                _backend.Journal.RemoveAll(entry => entry.Sequence <= throughSequence);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask QuarantineAsync(string reason, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            CheckWritable(ct);
            lock (_backend.Gate)
            {
                CheckWritableLocked();
                _backend.Quarantined = true;
                _backend.QuarantineReasonValue = reason.Length <= 500 ? reason : reason[..500];
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            lock (_backend.Gate)
            {
                if (_writerToken is not null && _backend.WriterToken == _writerToken)
                    _backend.WriterToken = null;
                _disposed = true;
            }
            return ValueTask.CompletedTask;
        }

        private void CheckUsable(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private void CheckWritable(CancellationToken ct)
        {
            CheckUsable(ct);
            if (Mode != AiStateStoreOpenMode.ReadWrite)
                throw Error(AiRuntimeStateReasonCodes.ReadOnly, "State Store wurde read-only geöffnet.");
        }

        private void CheckWritableLocked()
        {
            if (_writerToken is null || _backend.WriterToken != _writerToken)
                throw Error(AiRuntimeStateReasonCodes.StoreLocked, "Writer-Lock ist nicht mehr gültig.");
            if (_backend.Quarantined)
                throw Error(AiRuntimeStateReasonCodes.Quarantined, "State Store ist quarantänisiert.");
        }

        private void EnsureQuotaLocked(long newPayloadBytes, bool replacingSnapshot)
        {
            var snapshotBytes = replacingSnapshot ? 0 : _backend.Snapshot?.Payload.LongLength ?? 0;
            var journalBytes = _backend.Journal.Sum(entry => entry.Payload.LongLength);
            if (snapshotBytes + journalBytes + newPayloadBytes > _maxStoreBytes)
                throw Error(AiRuntimeStateReasonCodes.QuotaExceeded, "State-Store-Quota würde überschritten.");
        }
    }

    private static void ValidateSchema(int schemaVersion)
    {
        if (!AiRuntimeStateSchema.IsSupported(schemaVersion))
            throw Error(AiRuntimeStateReasonCodes.SchemaUnsupported,
                $"Schema-Version {schemaVersion} wird nicht unterstützt.");
    }

    private static void ValidatePayload(byte[] payload, string parameter)
    {
        if (payload is null) throw new ArgumentNullException(parameter);
    }

    private static void ValidateUtc(DateTime value, string parameter)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Zeitpunkt muss UTC sein.", parameter);
    }

    private static void ValidateOptionalChecksum(string? checksum, string parameter)
    {
        if (checksum is not null) ValidateChecksum(checksum, parameter);
    }

    private static void ValidateChecksum(string checksum, string parameter)
    {
        if (checksum is null || checksum.Length != 64 || checksum.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("SHA-256-Prüfsumme muss aus 64 Hex-Zeichen bestehen.", parameter);
    }

    private static AiRuntimeStateManifest Clone(AiRuntimeStateManifest manifest) => new()
    {
        SchemaVersion = manifest.SchemaVersion,
        StoreId = manifest.StoreId,
        RuntimeInstanceId = manifest.RuntimeInstanceId,
        LastSequence = manifest.LastSequence,
        SnapshotSequence = manifest.SnapshotSequence,
        CreatedAtUtc = manifest.CreatedAtUtc,
        UpdatedAtUtc = manifest.UpdatedAtUtc,
        SnapshotChecksumSha256 = manifest.SnapshotChecksumSha256,
        FeatureFlags = new Dictionary<string, bool>(manifest.FeatureFlags, StringComparer.Ordinal)
    };

    private static AiRuntimeStateSnapshot Clone(AiRuntimeStateSnapshot snapshot) => new()
    {
        SchemaVersion = snapshot.SchemaVersion,
        Sequence = snapshot.Sequence,
        CreatedAtUtc = snapshot.CreatedAtUtc,
        Payload = snapshot.Payload.ToArray(),
        ChecksumSha256 = snapshot.ChecksumSha256
    };

    private static AiRuntimeJournalEntry Clone(AiRuntimeJournalEntry entry) => new()
    {
        SchemaVersion = entry.SchemaVersion,
        Sequence = entry.Sequence,
        OperationId = entry.OperationId,
        EventType = entry.EventType,
        OccurredAtUtc = entry.OccurredAtUtc,
        IsProtected = entry.IsProtected,
        Payload = entry.Payload.ToArray(),
        ChecksumSha256 = entry.ChecksumSha256
    };

    private static AiStateStoreException Error(string code, string message) => new(code, message);
}
