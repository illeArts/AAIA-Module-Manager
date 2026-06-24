using System.Text.Json;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using AAIA.ModuleManager.Services.Ai.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase10DurableTransactionTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AppendFailure_DoesNotApplyMutationAndLocksFurtherWrites()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var inner = await OpenWriter(backend, "writer");
        await using var fault = new FaultSession(inner) { FailAppend = true };
        await using var transaction = await Recover(fault);

        await Assert.ThrowsAsync<IOException>(async () => await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, TaskPayload("task")));

        Assert.Empty(transaction.CreateCurrentSnapshot(Now).Tasks);
        Assert.Equal(AiRuntimeRecoveryStatus.RecoveryRequired, transaction.Status);
        Assert.Equal(0, backend.JournalCount);
        await Assert.ThrowsAsync<AiStateStoreException>(async () => await transaction.CommitAsync(
            "second", AiDurableMutationType.TaskCreated, TaskPayload("second")));
    }

    [Fact]
    public async Task FlushFailure_DoesNotApplyMutationAndRequiresRecovery()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var inner = await OpenWriter(backend, "writer");
        await using var fault = new FaultSession(inner) { FailFlush = true };
        await using var transaction = await Recover(fault);

        await Assert.ThrowsAsync<IOException>(async () => await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, TaskPayload("task")));

        Assert.Empty(transaction.CreateCurrentSnapshot(Now).Tasks);
        Assert.Equal(AiRuntimeRecoveryStatus.RecoveryRequired, transaction.Status);
        Assert.Equal(0, backend.LastFlushedSequence);
    }

    [Fact]
    public async Task SuccessfulCommit_AppendsFlushesAndThenApplies()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var inner = await OpenWriter(backend, "writer");
        await using var observed = new FaultSession(inner);
        await using var transaction = await Recover(observed);
        observed.Calls.Clear();

        var result = await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, TaskPayload("task"));

        Assert.True(result.Applied);
        Assert.Equal(1, result.Sequence);
        Assert.Equal(new[] { "append:1", "flush" }, observed.Calls.Take(2).ToArray());
        Assert.Equal("task", Assert.Single(transaction.CreateCurrentSnapshot(Now).Tasks).Id);
        Assert.Equal(1, backend.LastFlushedSequence);
    }

    [Fact]
    public async Task Restart_ReplaysFlushedDeltaAfterLastSnapshot()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using (var transaction = await NewTransaction(backend, "first"))
            await transaction.CommitAsync(
                "operation", AiDurableMutationType.TaskCreated, TaskPayload("task"));

        await using var restarted = await NewTransaction(backend, "second");

        Assert.Equal(1, restarted.LastSequence);
        Assert.Equal("task", Assert.Single(restarted.CreateCurrentSnapshot(Now).Tasks).Id);
    }

    [Fact]
    public async Task DuplicateOperation_IsNotAppendedOrAppliedTwice()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var transaction = await NewTransaction(backend, "writer");
        var payload = TaskPayload("task");

        var first = await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, payload);
        var duplicate = await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, payload);

        Assert.True(first.Applied);
        Assert.False(duplicate.Applied);
        Assert.Equal(1, duplicate.Sequence);
        Assert.Equal(1, backend.JournalCount);
        Assert.Single(transaction.CreateCurrentSnapshot(Now).Tasks);
    }

    [Fact]
    public async Task ConflictingOperation_IsRejectedBeforeStoreWrite()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var transaction = await NewTransaction(backend, "writer");
        await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, TaskPayload("first"));

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await transaction.CommitAsync(
                "operation", AiDurableMutationType.TaskCreated, TaskPayload("other")));

        Assert.Equal(AiRuntimeStateReasonCodes.OperationConflict, error.ReasonCode);
        Assert.Equal(1, backend.JournalCount);
    }

    [Fact]
    public async Task SensitivePayload_IsRejectedBeforeStoreWrite()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var transaction = await NewTransaction(backend, "writer");
        var payload = TaskPayload("task");
        payload = new AiTaskMutationPayload
        {
            Task = new AiDurableTaskSnapshot
            {
                Id = payload.Task.Id,
                Title = "api_key=abcdefgh12345678",
                Status = payload.Task.Status,
                CreatedAtUtc = payload.Task.CreatedAtUtc,
                UpdatedAtUtc = payload.Task.UpdatedAtUtc
            }
        };

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await transaction.CommitAsync(
                "operation", AiDurableMutationType.TaskCreated, payload));

        Assert.Equal(AiRuntimeStateReasonCodes.PayloadRejected, error.ReasonCode);
        Assert.Equal(0, backend.JournalCount);
        Assert.Empty(transaction.CreateCurrentSnapshot(Now).Tasks);
    }

    [Fact]
    public async Task EventThreshold_CreatesVerifiesAndCompactsSnapshot()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var inner = await OpenWriter(backend, "writer");
        await using var observed = new FaultSession(inner);
        await using var transaction = await Recover(observed, threshold: 2);
        observed.Calls.Clear();

        var first = await transaction.CommitAsync(
            "one", AiDurableMutationType.TaskCreated, TaskPayload("one"));
        var second = await transaction.CommitAsync(
            "two", AiDurableMutationType.TaskCreated, TaskPayload("two"));

        Assert.False(first.SnapshotCreated);
        Assert.True(second.SnapshotCreated);
        Assert.Equal(2, transaction.SnapshotSequence);
        Assert.Equal(0, backend.JournalCount);
        Assert.True(observed.Calls.IndexOf("snapshot:2") < observed.Calls.IndexOf("compact:2"));
        Assert.True(observed.Calls.IndexOf("manifest:2") < observed.Calls.IndexOf("compact:2"));
    }

    [Fact]
    public async Task TimeThreshold_CanBeTriggeredWithoutAnotherMutation()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var clock = new MutableTimeProvider(Now);
        await using var transaction = await NewTransaction(
            backend, "writer", threshold: 1000, clock: clock);
        await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, TaskPayload("task"));
        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.True(await transaction.SnapshotIfDueAsync());
        Assert.Equal(1, transaction.SnapshotSequence);
        Assert.Equal(0, backend.JournalCount);
    }

    [Fact]
    public async Task CompactFailure_LeavesVerifiedSnapshotAndRecoveryPossible()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var inner = await OpenWriter(backend, "writer");
        await using (var fault = new FaultSession(inner) { FailCompact = true })
        await using (var transaction = await Recover(fault, threshold: 1))
        {
            var result = await transaction.CommitAsync(
                "operation", AiDurableMutationType.TaskCreated, TaskPayload("task"));
            Assert.True(result.Applied);
            Assert.True(result.SnapshotDeferred);
            Assert.Equal(1, backend.JournalCount);
        }

        await using var restarted = await NewTransaction(backend, "second");
        Assert.Equal("task", Assert.Single(restarted.CreateCurrentSnapshot(Now).Tasks).Id);
    }

    [Fact]
    public async Task Phase9CheckpointFollowedByDelta_RemainsRecoverable()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using (var writer = await OpenWriter(backend, "legacy"))
        {
            var phase9 = new AiDurableOrchestrationSnapshot
            {
                CreatedAtUtc = Now,
                Tasks = new[] { Task("legacy") }
            };
            var entry = AiRuntimeStateCodec.CreateJournalEntry(
                1, "legacy-checkpoint", AiDurableMutationTransactionCoordinator.Phase9CheckpointEventType,
                Now, false, JsonSerializer.SerializeToUtf8Bytes(phase9));
            await writer.AppendJournalAsync(entry);
            await writer.FlushAsync();
        }

        await using (var migrated = await NewTransaction(backend, "migration"))
        {
            Assert.Equal("legacy", Assert.Single(migrated.CreateCurrentSnapshot(Now).Tasks).Id);
            await migrated.CommitAsync(
                "new-delta", AiDurableMutationType.TaskCreated, TaskPayload("new"));
        }

        await using var restarted = await NewTransaction(backend, "restart");
        Assert.Equal(new[] { "legacy", "new" },
            restarted.CreateCurrentSnapshot(Now).Tasks.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ShutdownSnapshot_CompactsPendingDeltas()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var transaction = await NewTransaction(backend, "writer");
        await transaction.CommitAsync(
            "operation", AiDurableMutationType.TaskCreated, TaskPayload("task"));

        await transaction.CreateShutdownSnapshotAsync();

        Assert.Equal(1, transaction.SnapshotSequence);
        Assert.Equal(0, backend.JournalCount);
    }

    [Fact]
    public async Task ParallelCommits_ProduceGaplessDurableSequence()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var transaction = await NewTransaction(backend, "writer", threshold: 1000);

        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 64).Select(index =>
            transaction.CommitAsync(
                $"operation-{index}", AiDurableMutationType.TaskCreated,
                TaskPayload($"task-{index}")).AsTask()));

        Assert.Equal(Enumerable.Range(1, 64).Select(value => (long)value),
            results.Select(item => item.Sequence).OrderBy(value => value));
        Assert.Equal(64, backend.JournalCount);
        Assert.Equal(64, transaction.CreateCurrentSnapshot(Now).Tasks.Count);
    }

    [Fact]
    public async Task PartialFileJournalTail_IsIsolatedAndNotAppliedAfterCrash()
    {
        using var temp = new TempDirectory();
        var options = Options();
        var crashingStore = new AiLocalFileRuntimeStateStore(
            temp.Path, "store", options,
            new OneShotFileFault(AiFileStateStoreFaultPoint.AfterJournalLength));
        await using (var transaction = await AiDurableMutationTransactionCoordinator.RecoverAsync(
                         await crashingStore.OpenAsync(AiStateStoreOpenMode.ReadWrite, "crash"), options,
                         new MutableTimeProvider(Now)))
        {
            await Assert.ThrowsAsync<InjectedFileCrash>(async () => await transaction.CommitAsync(
                "operation", AiDurableMutationType.TaskCreated, TaskPayload("task")));
            Assert.Empty(transaction.CreateCurrentSnapshot(Now).Tasks);
        }

        var recoveredStore = new AiLocalFileRuntimeStateStore(temp.Path, "store", options);
        await using var recovered = await AiDurableMutationTransactionCoordinator.RecoverAsync(
            await recoveredStore.OpenAsync(AiStateStoreOpenMode.ReadWrite, "recovery"), options,
            new MutableTimeProvider(Now));

        Assert.Equal(0, recovered.LastSequence);
        Assert.Empty(recovered.CreateCurrentSnapshot(Now).Tasks);
        Assert.NotEmpty(Directory.GetFiles(
            Path.Combine(temp.Path, "quarantine"), "journal-tail-*.bin"));
    }

    [Fact]
    public async Task CompleteFileJournalFrame_IsReplayedWhenCrashOccursBeforeReturn()
    {
        using var temp = new TempDirectory();
        var options = Options();
        var crashingStore = new AiLocalFileRuntimeStateStore(
            temp.Path, "store", options,
            new OneShotFileFault(AiFileStateStoreFaultPoint.AfterJournalFrame));
        await using (var transaction = await AiDurableMutationTransactionCoordinator.RecoverAsync(
                         await crashingStore.OpenAsync(AiStateStoreOpenMode.ReadWrite, "crash"), options,
                         new MutableTimeProvider(Now)))
        {
            await Assert.ThrowsAsync<InjectedFileCrash>(async () => await transaction.CommitAsync(
                "operation", AiDurableMutationType.TaskCreated, TaskPayload("task")));
            Assert.Empty(transaction.CreateCurrentSnapshot(Now).Tasks);
        }

        var recoveredStore = new AiLocalFileRuntimeStateStore(temp.Path, "store", options);
        await using var recovered = await AiDurableMutationTransactionCoordinator.RecoverAsync(
            await recoveredStore.OpenAsync(AiStateStoreOpenMode.ReadWrite, "recovery"), options,
            new MutableTimeProvider(Now));

        Assert.Equal(1, recovered.LastSequence);
        Assert.Equal("task", Assert.Single(recovered.CreateCurrentSnapshot(Now).Tasks).Id);
    }

    private static async ValueTask<AiDurableMutationTransactionCoordinator> NewTransaction(
        AiInMemoryRuntimeStateStoreBackend backend,
        string runtimeId,
        int threshold = 1000,
        TimeProvider? clock = null)
        => await Recover(await OpenWriter(backend, runtimeId), threshold, clock);

    private static async ValueTask<AiDurableMutationTransactionCoordinator> Recover(
        IAiRuntimeStateStoreSession session,
        int threshold = 1000,
        TimeProvider? clock = null)
        => await AiDurableMutationTransactionCoordinator.RecoverAsync(
            session, Options(threshold),
            clock ?? new MutableTimeProvider(Now));

    private static AiRuntimePersistenceOptions Options(int threshold = 1000) => new()
    {
        Enabled = true,
        MaxStoreBytes = 8 * 1024 * 1024,
        MaxProtectedPayloadBytes = 1024 * 1024,
        SnapshotJournalEntryThreshold = threshold,
        SnapshotInterval = TimeSpan.FromMinutes(10)
    };

    private static async ValueTask<IAiRuntimeStateStoreSession> OpenWriter(
        AiInMemoryRuntimeStateStoreBackend backend,
        string runtimeId)
        => await new AiInMemoryRuntimeStateStore(backend)
            .OpenAsync(AiStateStoreOpenMode.ReadWrite, runtimeId);

    private static AiTaskMutationPayload TaskPayload(string id)
        => new() { Task = Task(id) };

    private static AiDurableTaskSnapshot Task(string id) => new()
    {
        Id = id,
        Title = id,
        Status = AiTaskStatus.Pending,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTime utcNow) => _utcNow = new DateTimeOffset(utcNow);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class FaultSession : IAiRuntimeStateStoreSession
    {
        private readonly IAiRuntimeStateStoreSession _inner;

        public bool FailAppend { get; init; }
        public bool FailFlush { get; init; }
        public bool FailCompact { get; init; }
        public List<string> Calls { get; } = new();
        public string StoreId => _inner.StoreId;
        public string RuntimeInstanceId => _inner.RuntimeInstanceId;
        public AiStateStoreOpenMode Mode => _inner.Mode;
        public bool IsQuarantined => _inner.IsQuarantined;
        public string? QuarantineReason => _inner.QuarantineReason;

        public FaultSession(IAiRuntimeStateStoreSession inner) => _inner = inner;

        public ValueTask<AiRuntimeStateManifest?> LoadManifestAsync(CancellationToken ct = default)
            => _inner.LoadManifestAsync(ct);

        public ValueTask WriteManifestAsync(
            AiRuntimeStateManifest manifest, CancellationToken ct = default)
        {
            Calls.Add($"manifest:{manifest.SnapshotSequence}");
            return _inner.WriteManifestAsync(manifest, ct);
        }

        public ValueTask<AiRuntimeStateSnapshot?> LoadSnapshotAsync(CancellationToken ct = default)
            => _inner.LoadSnapshotAsync(ct);

        public ValueTask WriteSnapshotAsync(
            AiRuntimeStateSnapshot snapshot, CancellationToken ct = default)
        {
            Calls.Add($"snapshot:{snapshot.Sequence}");
            return _inner.WriteSnapshotAsync(snapshot, ct);
        }

        public IAsyncEnumerable<AiRuntimeJournalEntry> ReadJournalAsync(
            long afterSequence, CancellationToken ct = default)
            => _inner.ReadJournalAsync(afterSequence, ct);

        public ValueTask AppendJournalAsync(
            AiRuntimeJournalEntry entry, CancellationToken ct = default)
        {
            Calls.Add($"append:{entry.Sequence}");
            if (FailAppend) throw new IOException("Injected append failure.");
            return _inner.AppendJournalAsync(entry, ct);
        }

        public ValueTask FlushAsync(CancellationToken ct = default)
        {
            Calls.Add("flush");
            if (FailFlush) throw new IOException("Injected flush failure.");
            return _inner.FlushAsync(ct);
        }

        public ValueTask CompactAsync(long throughSequence, CancellationToken ct = default)
        {
            Calls.Add($"compact:{throughSequence}");
            if (FailCompact) throw new IOException("Injected compact failure.");
            return _inner.CompactAsync(throughSequence, ct);
        }

        public ValueTask QuarantineAsync(string reason, CancellationToken ct = default)
            => _inner.QuarantineAsync(reason, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class OneShotFileFault(AiFileStateStoreFaultPoint target)
        : IAiFileStateStoreFaultInjector
    {
        private bool _fired;

        public void Inject(AiFileStateStoreFaultPoint point)
        {
            if (!_fired && point == target)
            {
                _fired = true;
                throw new InjectedFileCrash();
            }
        }
    }

    private sealed class InjectedFileCrash : Exception;

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "aaia-phase10-tests", Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
