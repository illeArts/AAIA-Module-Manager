using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using AAIA.ModuleManager.Services.Ai.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase10OperationalConformanceTests
{
    private static readonly DateTime Now = new(2026, 6, 25, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BusyWriter_ReturnsBackpressureAndDiagnosticsRemainReadable()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var blockingStore = new BlockingFlushStore(new AiInMemoryRuntimeStateStore(backend));
        var runtime = Runtime();
        runtime.Tasks.Create("first");
        await using var coordinator = new AiRuntimePersistenceCoordinator(
            runtime, blockingStore, new PassthroughProtector(), new AiRuntimePersistenceOptions
            {
                Enabled = true,
                WriterBackpressureTimeout = TimeSpan.FromMilliseconds(10)
            }, "runtime");
        await coordinator.InitializeAsync();

        var first = coordinator.PersistMutationAsync("first").AsTask();
        await blockingStore.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        runtime.Tasks.Create("second");
        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await coordinator.PersistMutationAsync("second"));
        var diagnostics = await blockingStore.GetDiagnosticsAsync();
        blockingStore.ReleaseFlush();
        await first;

        Assert.Equal(AiRuntimeStateReasonCodes.Backpressure, error.ReasonCode);
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, diagnostics.Status);
    }

    [Fact]
    public async Task ShutdownTimeout_ReportsIncompleteShutdown()
    {
        var runtime = Runtime();
        await using var lifecycle = new AiRuntimeLifecycle(
            runtime,
            adapterStart: _ => ValueTask.CompletedTask,
            adapterStop: async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            },
            options: new AiRuntimeLifecycleOptions { ShutdownTimeout = TimeSpan.FromMilliseconds(10) });
        await lifecycle.InitializeAsync();

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await lifecycle.StopAsync());

        Assert.Equal(AiRuntimeStateReasonCodes.ShutdownIncomplete, error.ReasonCode);
        Assert.Equal(AiRuntimeRecoveryStatus.RecoveryFailed, lifecycle.Status);
    }

    [Fact]
    public async Task RepeatedCrashRestart_DoesNotCreateSequenceGap()
    {
        using var temp = new TempDirectory();
        var options = Options();

        for (var index = 0; index < 5; index++)
        {
            var crashing = new AiLocalFileRuntimeStateStore(
                temp.Path, "store", options,
                new OneShotFileFault(AiFileStateStoreFaultPoint.AfterJournalFrame));
            await using (var transaction = await AiDurableMutationTransactionCoordinator.RecoverAsync(
                             await crashing.OpenAsync(AiStateStoreOpenMode.ReadWrite, $"crash-{index}"),
                             options))
            {
                await Assert.ThrowsAsync<InjectedFileCrash>(async () =>
                    await transaction.CommitAsync(
                        $"operation-{index}",
                        AiDurableMutationType.TaskCreated,
                        TaskPayload($"task-{index}")));
            }

            var recovered = new AiLocalFileRuntimeStateStore(temp.Path, "store", options);
            await using var replay = await AiDurableMutationTransactionCoordinator.RecoverAsync(
                await recovered.OpenAsync(AiStateStoreOpenMode.ReadWrite, $"recovery-{index}"),
                options);

            Assert.Equal(index + 1, replay.LastSequence);
            Assert.Equal(index + 1, replay.CreateCurrentSnapshot(Now).Tasks.Count);
        }
    }

    private static AiRuntimeService Runtime() => new(
        new AiToolRegistry(), new AiSessionManager(), new AiCapabilityManager(),
        new AiPermissionEngine(), new AiWorkspaceLockService(),
        new AiRuntimeEventBus(), new AiAuditService());

    private static AiRuntimePersistenceOptions Options() => new()
    {
        Enabled = true,
        MaxStoreBytes = 8 * 1024 * 1024,
        MaxProtectedPayloadBytes = 1024 * 1024,
        SnapshotJournalEntryThreshold = 1000,
        SnapshotInterval = TimeSpan.FromMinutes(10)
    };

    private static AiTaskMutationPayload TaskPayload(string id)
        => new()
        {
            Task = new AiDurableTaskSnapshot
            {
                Id = id,
                Title = id,
                Status = AiTaskStatus.Pending,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            }
        };

    private sealed class BlockingFlushStore(IAiRuntimeStateStore inner)
        : IAiRuntimeStateStore, IAiRuntimeStateMaintenanceStore
    {
        private readonly TaskCompletionSource _releaseFlush =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;

        public TaskCompletionSource FlushEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string StoreId => inner.StoreId;

        public async ValueTask<IAiRuntimeStateStoreSession> OpenAsync(
            AiStateStoreOpenMode mode,
            string runtimeInstanceId,
            CancellationToken ct = default)
            => new BlockingFlushSession(
                await inner.OpenAsync(mode, runtimeInstanceId, ct),
                this);

        public void ReleaseFlush() => _releaseFlush.TrySetResult();

        public ValueTask<AiRuntimeStateDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default)
            => inner is IAiRuntimeStateMaintenanceStore maintenance
                ? maintenance.GetDiagnosticsAsync(ct)
                : ValueTask.FromResult(new AiRuntimeStateDiagnostics
                {
                    StoreId = StoreId,
                    Status = AiRuntimeRecoveryStatus.Ready
                });

        public ValueTask<AiStateStoreBackupResult> CreateBackupAsync(
            string runtimeInstanceId,
            CancellationToken ct = default)
            => ((IAiRuntimeStateMaintenanceStore)inner).CreateBackupAsync(runtimeInstanceId, ct);

        public ValueTask<AiStateStoreRepairResult> RepairAsync(
            string runtimeInstanceId,
            string backupId,
            CancellationToken ct = default)
            => ((IAiRuntimeStateMaintenanceStore)inner).RepairAsync(runtimeInstanceId, backupId, ct);

        private async ValueTask BlockFirstFlushAsync(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                FlushEntered.TrySetResult();
                await _releaseFlush.Task.WaitAsync(ct);
            }
        }

        private sealed class BlockingFlushSession(
            IAiRuntimeStateStoreSession inner,
            BlockingFlushStore owner) : IAiRuntimeStateStoreSession
        {
            public string StoreId => inner.StoreId;
            public string RuntimeInstanceId => inner.RuntimeInstanceId;
            public AiStateStoreOpenMode Mode => inner.Mode;
            public bool IsQuarantined => inner.IsQuarantined;
            public string? QuarantineReason => inner.QuarantineReason;
            public ValueTask<AiRuntimeStateManifest?> LoadManifestAsync(CancellationToken ct = default)
                => inner.LoadManifestAsync(ct);
            public ValueTask WriteManifestAsync(AiRuntimeStateManifest manifest, CancellationToken ct = default)
                => inner.WriteManifestAsync(manifest, ct);
            public ValueTask<AiRuntimeStateSnapshot?> LoadSnapshotAsync(CancellationToken ct = default)
                => inner.LoadSnapshotAsync(ct);
            public ValueTask WriteSnapshotAsync(AiRuntimeStateSnapshot snapshot, CancellationToken ct = default)
                => inner.WriteSnapshotAsync(snapshot, ct);
            public IAsyncEnumerable<AiRuntimeJournalEntry> ReadJournalAsync(long afterSequence, CancellationToken ct = default)
                => inner.ReadJournalAsync(afterSequence, ct);
            public ValueTask AppendJournalAsync(AiRuntimeJournalEntry entry, CancellationToken ct = default)
                => inner.AppendJournalAsync(entry, ct);
            public async ValueTask FlushAsync(CancellationToken ct = default)
            {
                await owner.BlockFirstFlushAsync(ct);
                await inner.FlushAsync(ct);
            }
            public ValueTask CompactAsync(long throughSequence, CancellationToken ct = default)
                => inner.CompactAsync(throughSequence, ct);
            public ValueTask QuarantineAsync(string reason, CancellationToken ct = default)
                => inner.QuarantineAsync(reason, ct);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class PassthroughProtector : IAiStateProtector
    {
        public string ProtectorId => "test";
        public ValueTask<AiProtectedStatePayload> ProtectAsync(
            ReadOnlyMemory<byte> plaintext, AiStateProtectionContext context, CancellationToken ct = default)
            => ValueTask.FromResult(new AiProtectedStatePayload
            {
                ProtectorId = ProtectorId,
                Ciphertext = plaintext.ToArray()
            });
        public ValueTask<byte[]> UnprotectAsync(
            AiProtectedStatePayload protectedPayload, AiStateProtectionContext context, CancellationToken ct = default)
            => ValueTask.FromResult(protectedPayload.Ciphertext.ToArray());
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
            System.IO.Path.GetTempPath(), "aaia-phase10-conformance", Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
