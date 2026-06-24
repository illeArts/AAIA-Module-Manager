using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using AAIA.ModuleManager.Services.Ai.Persistence;
using System.Text.Json;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9FileStateStoreTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DisabledStore_RejectsWriterWithoutCreatingDirectory()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "state");
        var store = new AiLocalFileRuntimeStateStore(root, "test");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"));

        Assert.Equal(AiRuntimeStateReasonCodes.Disabled, error.ReasonCode);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task StoreConfiguration_IsFrozenAtConstruction()
    {
        using var temp = new TempDirectory();
        var options = EnabledOptions();
        var store = new AiLocalFileRuntimeStateStore(temp.Path, "test", options);
        options.Enabled = false;
        options.MaxStoreBytes = 1;

        await using var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await writer.AppendJournalAsync(Entry(1, "op-1"));

        Assert.Single(await ReadAll(writer));
    }

    [Fact]
    public void StoreInsideGitWorkspace_IsRejected()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, ".git"));

        Assert.Throws<ArgumentException>(() =>
            new AiLocalFileRuntimeStateStore(Path.Combine(temp.Path, "state"), "test", EnabledOptions()));
    }

    [Fact]
    public async Task FileLock_AllowsReadersButRejectsSecondWriter()
    {
        using var temp = new TempDirectory();
        var first = Store(temp.Path);
        var second = Store(temp.Path);
        await using var writer = await first.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-1");
        await using var reader = await second.OpenAsync(AiStateStoreOpenMode.ReadOnly, "diagnostics");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await second.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-2"));

        Assert.Equal(AiRuntimeStateReasonCodes.StoreLocked, error.ReasonCode);
        Assert.Equal(AiStateStoreOpenMode.ReadOnly, reader.Mode);
    }

    [Fact]
    public async Task Journal_RoundTripsAcrossStoreInstances()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-1"))
        {
            await writer.AppendJournalAsync(Entry(1, "op-1"));
            await writer.AppendJournalAsync(Entry(2, "op-2"));
        }

        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "diagnostics");
        var entries = await ReadAll(reader);

        Assert.Equal(new long[] { 1, 2 }, entries.Select(entry => entry.Sequence));
        Assert.Equal(new[] { "op-1", "op-2" }, entries.Select(entry => entry.OperationId));
    }

    [Fact]
    public async Task ManifestAndSnapshot_RoundTripAcrossRestart()
    {
        using var temp = new TempDirectory();
        var snapshot = AiRuntimeStateCodec.CreateSnapshot(1, Now, new byte[] { 4, 5, 6 });
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-1"))
        {
            await writer.AppendJournalAsync(Entry(1, "op-1"));
            await writer.WriteSnapshotAsync(snapshot);
            await writer.WriteManifestAsync(Manifest("runtime-1", 1, 1, snapshot.ChecksumSha256));
        }

        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "diagnostics");
        var restoredSnapshot = await reader.LoadSnapshotAsync();
        var restoredManifest = await reader.LoadManifestAsync();

        Assert.Equal(new byte[] { 4, 5, 6 }, restoredSnapshot!.Payload);
        Assert.Equal(snapshot.ChecksumSha256, restoredManifest!.SnapshotChecksumSha256);
        Assert.Equal(1, restoredManifest.LastSequence);
    }

    [Fact]
    public async Task ReadOnlyFileSession_CannotMutate()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime")) { }
        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await reader.AppendJournalAsync(Entry(1, "op-1")));

        Assert.Equal(AiRuntimeStateReasonCodes.ReadOnly, error.ReasonCode);
    }

    [Theory]
    [InlineData(AiFileStateStoreFaultPoint.BeforeTempWrite, false)]
    [InlineData(AiFileStateStoreFaultPoint.AfterTempFlush, false)]
    [InlineData(AiFileStateStoreFaultPoint.AfterTempVerify, false)]
    [InlineData(AiFileStateStoreFaultPoint.BeforeAtomicReplace, false)]
    [InlineData(AiFileStateStoreFaultPoint.AfterAtomicReplace, true)]
    public async Task SnapshotCrashBoundary_LeavesCompleteOldOrNewState(
        AiFileStateStoreFaultPoint point,
        bool expectsNew)
    {
        using var temp = new TempDirectory();
        var oldSnapshot = AiRuntimeStateCodec.CreateSnapshot(1, Now, new byte[] { 1 });
        var newSnapshot = AiRuntimeStateCodec.CreateSnapshot(2, Now.AddMinutes(1), new byte[] { 2 });
        await using (var setup = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "setup"))
        {
            await setup.AppendJournalAsync(Entry(1, "op-1"));
            await setup.AppendJournalAsync(Entry(2, "op-2"));
            await setup.WriteSnapshotAsync(oldSnapshot);
        }

        var injector = new OneShotFaultInjector(point);
        await using (var writer = await Store(temp.Path, injector).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
        {
            await Assert.ThrowsAsync<InjectedCrashException>(async () =>
                await writer.WriteSnapshotAsync(newSnapshot));
        }

        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");
        var restored = await reader.LoadSnapshotAsync();

        Assert.Equal(expectsNew ? new byte[] { 2 } : new byte[] { 1 }, restored!.Payload);
    }

    [Fact]
    public async Task IncompleteJournalTail_IsQuarantinedAndTruncatedByNextWriter()
    {
        using var temp = new TempDirectory();
        await using (var setup = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "setup"))
            await setup.AppendJournalAsync(Entry(1, "op-1"));

        await using (var crashing = await Store(
            temp.Path, new OneShotFaultInjector(AiFileStateStoreFaultPoint.AfterJournalLength))
            .OpenAsync(AiStateStoreOpenMode.ReadWrite, "crash"))
        {
            await Assert.ThrowsAsync<InjectedCrashException>(async () =>
                await crashing.AppendJournalAsync(Entry(2, "op-2")));
        }

        await using (var recovered = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "recovery"))
        {
            Assert.Single(await ReadAll(recovered));
            await recovered.AppendJournalAsync(Entry(2, "op-2"));
        }

        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(temp.Path, "quarantine"), "journal-tail-*.bin"));
    }

    [Fact]
    public async Task CompleteUnflushedJournalFrame_IsRecoveredAsCommittedRecord()
    {
        using var temp = new TempDirectory();
        await using (var setup = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "setup"))
            await setup.AppendJournalAsync(Entry(1, "op-1"));

        await using (var crashing = await Store(
            temp.Path, new OneShotFaultInjector(AiFileStateStoreFaultPoint.AfterJournalFrame))
            .OpenAsync(AiStateStoreOpenMode.ReadWrite, "crash"))
        {
            await Assert.ThrowsAsync<InjectedCrashException>(async () =>
                await crashing.AppendJournalAsync(Entry(2, "op-2")));
        }

        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");
        Assert.Equal(new long[] { 1, 2 }, (await ReadAll(reader)).Select(entry => entry.Sequence));
    }

    [Fact]
    public async Task CorruptJournalFrame_FailsClosed()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
            await writer.AppendJournalAsync(Entry(1, "op-1", new byte[] { 1, 2, 3 }));
        var journalPath = Path.Combine(temp.Path, "journal.bin");
        var bytes = File.ReadAllBytes(journalPath);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(journalPath, bytes);

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "recovery"));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalChecksumFailed, error.ReasonCode);
    }

    [Fact]
    public async Task Compaction_IsAtomicAndPersistsAcrossRestart()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
        {
            await writer.AppendJournalAsync(Entry(1, "op-1"));
            await writer.AppendJournalAsync(Entry(2, "op-2"));
            await writer.WriteSnapshotAsync(AiRuntimeStateCodec.CreateSnapshot(1, Now, Array.Empty<byte>()));
            await writer.CompactAsync(1);
        }

        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");
        var remaining = Assert.Single(await ReadAll(reader));
        Assert.Equal(2, remaining.Sequence);
    }

    [Fact]
    public async Task Quarantine_BlocksFutureWritersAndKeepsDiagnosticsReadable()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
        {
            await writer.AppendJournalAsync(Entry(1, "op-1"));
            await writer.QuarantineAsync("checksum failed");
        }

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-2"));
        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");

        Assert.Equal(AiRuntimeStateReasonCodes.Quarantined, error.ReasonCode);
        Assert.Equal("checksum failed", reader.QuarantineReason);
        Assert.Single(await ReadAll(reader));
    }

    [Fact]
    public async Task QuarantineReason_RedactsSensitiveContent()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
            await writer.QuarantineAsync("Bearer abcdefghijklmnopqrstuvwxyz123456");

        await using var reader = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");

        Assert.Equal("redacted_sensitive_reason", reader.QuarantineReason);
    }

    [Fact]
    public async Task ManifestCannotHideMissingJournalSequence()
    {
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
            await writer.AppendJournalAsync(Entry(1, "op-1"));
        var manifest = Manifest("runtime", 2, 0, checksum: null);
        await File.WriteAllBytesAsync(
            Path.Combine(temp.Path, "manifest.json"),
            JsonSerializer.SerializeToUtf8Bytes(manifest));

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "recovery"));

        Assert.Equal(AiRuntimeStateReasonCodes.JournalGap, error.ReasonCode);
    }

    [Fact]
    public async Task StoreQuota_IsEnforcedBeforeJournalAppend()
    {
        using var temp = new TempDirectory();
        var options = EnabledOptions(maxStoreBytes: 128);
        var store = new AiLocalFileRuntimeStateStore(temp.Path, "test", options);
        await using var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await writer.AppendJournalAsync(Entry(1, "op-1", new byte[100])));

        Assert.Equal(AiRuntimeStateReasonCodes.QuotaExceeded, error.ReasonCode);
        Assert.Empty(await ReadAll(writer));
    }

    [Fact]
    public async Task StaleTempFiles_AreRemovedByNextWriter()
    {
        using var temp = new TempDirectory();
        await using (var setup = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "setup")) { }
        var stale = Path.Combine(temp.Path, "snapshot.bin.tmp-stale");
        await File.WriteAllTextAsync(stale, "partial");

        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime")) { }

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public async Task UnixStoreFiles_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory();
        await using (var writer = await Store(temp.Path).OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime"))
            await writer.AppendJournalAsync(Entry(1, "op-1"));

        var mode = File.GetUnixFileMode(Path.Combine(temp.Path, "journal.bin"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    private static AiLocalFileRuntimeStateStore Store(
        string root,
        IAiFileStateStoreFaultInjector? injector = null)
        => new(root, "test", EnabledOptions(), injector);

    private static AiRuntimePersistenceOptions EnabledOptions(long maxStoreBytes = 1024 * 1024) => new()
    {
        Enabled = true,
        MaxStoreBytes = maxStoreBytes,
        MaxProtectedPayloadBytes = (int)Math.Min(64 * 1024, maxStoreBytes)
    };

    private static AiRuntimeJournalEntry Entry(long sequence, string operationId, byte[]? payload = null)
        => AiRuntimeStateCodec.CreateJournalEntry(
            sequence, operationId, "test.event", Now, false, payload ?? Array.Empty<byte>(),
            maxPayloadBytes: 1024 * 1024);

    private static AiRuntimeStateManifest Manifest(
        string runtimeId,
        long lastSequence,
        long snapshotSequence,
        string? checksum) => new()
        {
            StoreId = "test",
            RuntimeInstanceId = runtimeId,
            LastSequence = lastSequence,
            SnapshotSequence = snapshotSequence,
            SnapshotChecksumSha256 = checksum,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

    private static async Task<IReadOnlyList<AiRuntimeJournalEntry>> ReadAll(
        IAiRuntimeStateStoreSession session)
    {
        var result = new List<AiRuntimeJournalEntry>();
        await foreach (var entry in session.ReadJournalAsync(0)) result.Add(entry);
        return result;
    }

    private sealed class OneShotFaultInjector(AiFileStateStoreFaultPoint target)
        : IAiFileStateStoreFaultInjector
    {
        private int _triggered;

        public void Inject(AiFileStateStoreFaultPoint point)
        {
            if (point == target && Interlocked.Exchange(ref _triggered, 1) == 0)
                throw new InjectedCrashException(point);
        }
    }

    private sealed class InjectedCrashException(AiFileStateStoreFaultPoint point)
        : Exception($"Injected crash at {point}");

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "aaia-state-tests", Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* test cleanup only */ }
        }
    }
}
