using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9StateStoreTests
{
    private const string Checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTime Now = new(2026, 6, 24, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PersistenceOptions_DefaultToDisabledAndBounded()
    {
        var options = new AiRuntimePersistenceOptions();

        Assert.False(options.Enabled);
        Assert.Equal(100L * 1024 * 1024, options.MaxStoreBytes);
        Assert.Equal(1024 * 1024, options.MaxProtectedPayloadBytes);
        Assert.Equal(10_000, options.MaxIdempotencyEntries);
    }

    [Fact]
    public void SchemaVersion_IsExplicitAndSupported()
    {
        Assert.Equal(1, AiRuntimeStateSchema.CurrentVersion);
        Assert.True(AiRuntimeStateSchema.IsSupported(1));
        Assert.False(AiRuntimeStateSchema.IsSupported(0));
        Assert.False(AiRuntimeStateSchema.IsSupported(2));
    }

    [Fact]
    public async Task SecondWriter_IsRejectedButReadOnlySessionIsAllowed()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var firstStore = new AiInMemoryRuntimeStateStore(backend);
        var secondStore = new AiInMemoryRuntimeStateStore(backend);
        await using var writer = await firstStore.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-1");
        await using var reader = await secondStore.OpenAsync(AiStateStoreOpenMode.ReadOnly, "diagnostics");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await secondStore.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-2"));

        Assert.Equal(AiRuntimeStateReasonCodes.StoreLocked, error.ReasonCode);
        Assert.Equal(AiStateStoreOpenMode.ReadOnly, reader.Mode);
    }

    [Fact]
    public async Task UnknownOpenMode_IsRejected()
    {
        var store = Store();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.OpenAsync((AiStateStoreOpenMode)99, "runtime"));
    }

    [Fact]
    public async Task DisposedWriter_ReleasesExclusiveLock()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var store = new AiInMemoryRuntimeStateStore(backend);
        var first = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-1");
        await first.DisposeAsync();

        await using var second = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-2");

        Assert.Equal("runtime-2", second.RuntimeInstanceId);
    }

    [Fact]
    public async Task ReadOnlySession_CannotMutateStore()
    {
        var store = Store();
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await session.AppendJournalAsync(Entry(1, "op-1")));

        Assert.Equal(AiRuntimeStateReasonCodes.ReadOnly, error.ReasonCode);
    }

    [Fact]
    public async Task Manifest_RoundTripsWithDefensiveFeatureFlagCopy()
    {
        var store = Store("store");
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        var flags = new Dictionary<string, bool> { ["tasks"] = true };
        await session.WriteManifestAsync(Manifest("store", "runtime", flags: flags));
        flags["tasks"] = false;

        var loaded = await session.LoadManifestAsync();

        Assert.NotNull(loaded);
        Assert.True(loaded!.FeatureFlags["tasks"]);
    }

    [Fact]
    public async Task UnsupportedManifestSchema_IsRejectedWithoutMutation()
    {
        var store = Store("store");
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        var unsupported = new AiRuntimeStateManifest
        {
            SchemaVersion = 2,
            StoreId = "store",
            RuntimeInstanceId = "runtime",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now
        };

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await session.WriteManifestAsync(unsupported));

        Assert.Equal(AiRuntimeStateReasonCodes.SchemaUnsupported, error.ReasonCode);
        Assert.Null(await session.LoadManifestAsync());
    }

    [Fact]
    public async Task ManifestWithoutFeatureFlags_IsRejected()
    {
        var store = Store("store");
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        var manifest = new AiRuntimeStateManifest
        {
            StoreId = "store",
            RuntimeInstanceId = "runtime",
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
            FeatureFlags = null!
        };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await session.WriteManifestAsync(manifest));
    }

    [Fact]
    public async Task Journal_RequiresContiguousSequenceAndReturnsDefensivePayloads()
    {
        var store = Store();
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        var entry = Entry(1, "op-1", new byte[] { 1, 2, 3 });
        await session.AppendJournalAsync(entry);
        entry.Payload[0] = 99;

        var loaded = Assert.Single(await ReadAll(session));
        loaded.Payload[1] = 88;
        var loadedAgain = Assert.Single(await ReadAll(session));

        Assert.Equal(new byte[] { 1, 2, 3 }, loadedAgain.Payload);
        var gap = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await session.AppendJournalAsync(Entry(3, "op-3")));
        Assert.Equal(AiRuntimeStateReasonCodes.JournalGap, gap.ReasonCode);
    }

    [Fact]
    public async Task DuplicateOperationId_IsRejected()
    {
        var store = Store();
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await session.AppendJournalAsync(Entry(1, "same"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.AppendJournalAsync(Entry(2, "same")));
    }

    [Fact]
    public async Task Snapshot_RoundTripsDefensivelyAndCannotLeadJournal()
    {
        var store = Store();
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await session.AppendJournalAsync(Entry(1, "op-1"));
        var snapshot = Snapshot(1, new byte[] { 4, 5, 6 });
        await session.WriteSnapshotAsync(snapshot);
        snapshot.Payload[0] = 99;

        var loaded = await session.LoadSnapshotAsync();

        Assert.Equal(new byte[] { 4, 5, 6 }, loaded!.Payload);
        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await session.WriteSnapshotAsync(Snapshot(2)));
        Assert.Equal(AiRuntimeStateReasonCodes.JournalGap, error.ReasonCode);
    }

    [Fact]
    public async Task Compaction_RequiresConfirmedSnapshot()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend();
        var store = new AiInMemoryRuntimeStateStore(backend);
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await session.AppendJournalAsync(Entry(1, "op-1"));
        await session.AppendJournalAsync(Entry(2, "op-2"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.CompactAsync(1));
        await session.WriteSnapshotAsync(Snapshot(1));
        await session.CompactAsync(1);

        Assert.Equal(1, backend.JournalCount);
        Assert.Equal(2, Assert.Single(await ReadAll(session, afterSequence: 1)).Sequence);
    }

    [Fact]
    public async Task Flush_TracksLastDurableSequence()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend();
        var store = new AiInMemoryRuntimeStateStore(backend);
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await session.AppendJournalAsync(Entry(1, "op-1"));

        Assert.Equal(0, backend.LastFlushedSequence);
        await session.FlushAsync();
        Assert.Equal(1, backend.LastFlushedSequence);
    }

    [Fact]
    public async Task QuotaFailure_DoesNotPoisonOperationId()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend();
        var store = new AiInMemoryRuntimeStateStore(backend, maxStoreBytes: 3);
        await using var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await session.AppendJournalAsync(Entry(1, "op-1", new byte[4])));
        Assert.Equal(AiRuntimeStateReasonCodes.QuotaExceeded, error.ReasonCode);

        await session.AppendJournalAsync(Entry(1, "op-1", new byte[3]));

        Assert.Single(await ReadAll(session));
    }

    [Fact]
    public async Task Quarantine_BlocksWritersButKeepsReadOnlyDiagnostics()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend();
        var store = new AiInMemoryRuntimeStateStore(backend);
        var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await writer.AppendJournalAsync(Entry(1, "op-1"));
        await writer.QuarantineAsync("checksum failed");
        Assert.True(writer.IsQuarantined);
        await writer.DisposeAsync();

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime-2"));
        await using var reader = await store.OpenAsync(AiStateStoreOpenMode.ReadOnly, "diagnostics");

        Assert.Equal(AiRuntimeStateReasonCodes.Quarantined, error.ReasonCode);
        Assert.Equal("checksum failed", reader.QuarantineReason);
        Assert.Single(await ReadAll(reader));
    }

    [Fact]
    public async Task DisposedSession_RejectsFurtherUse()
    {
        var store = Store();
        var session = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "runtime");
        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.LoadManifestAsync());
    }

    private static AiInMemoryRuntimeStateStore Store(string? storeId = null)
        => new(new AiInMemoryRuntimeStateStoreBackend(storeId));

    private static AiRuntimeStateManifest Manifest(
        string storeId,
        string runtimeId,
        long lastSequence = 0,
        IReadOnlyDictionary<string, bool>? flags = null) => new()
        {
            StoreId = storeId,
            RuntimeInstanceId = runtimeId,
            LastSequence = lastSequence,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
            FeatureFlags = flags ?? new Dictionary<string, bool>()
        };

    private static AiRuntimeJournalEntry Entry(long sequence, string operationId, byte[]? payload = null) => new()
    {
        Sequence = sequence,
        OperationId = operationId,
        EventType = "test.event",
        OccurredAtUtc = Now,
        Payload = payload ?? Array.Empty<byte>(),
        ChecksumSha256 = Checksum
    };

    private static AiRuntimeStateSnapshot Snapshot(long sequence, byte[]? payload = null) => new()
    {
        Sequence = sequence,
        CreatedAtUtc = Now,
        Payload = payload ?? Array.Empty<byte>(),
        ChecksumSha256 = Checksum
    };

    private static async Task<IReadOnlyList<AiRuntimeJournalEntry>> ReadAll(
        IAiRuntimeStateStoreSession session,
        long afterSequence = 0)
    {
        var result = new List<AiRuntimeJournalEntry>();
        await foreach (var entry in session.ReadJournalAsync(afterSequence)) result.Add(entry);
        return result;
    }
}
