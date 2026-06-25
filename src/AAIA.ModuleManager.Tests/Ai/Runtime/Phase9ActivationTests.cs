using System.Text;
using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using AAIA.ModuleManager.Services.Ai.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9ActivationTests
{
    [Fact]
    public async Task WindowsProtector_RoundTripsAndBindsCiphertextToContext()
    {
        if (!OperatingSystem.IsWindows()) return;
        var protector = new AiLocalUserStateProtector();
        var plaintext = Encoding.UTF8.GetBytes("confidential project input");
        var context = Context("store-a", "task-step", "task:0");

        var protectedPayload = await protector.ProtectAsync(plaintext, context);
        var restored = await protector.UnprotectAsync(protectedPayload, context);

        Assert.Equal(plaintext, restored);
        Assert.NotEqual(plaintext, protectedPayload.Ciphertext);
        await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await protector.UnprotectAsync(protectedPayload, Context("store-b", "task-step", "task:0")));
    }

    [Fact]
    public async Task DisabledCoordinator_DoesNotOpenOrBlockRuntime()
    {
        var runtime = Runtime();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend, enabled: false);

        var status = await coordinator.InitializeAsync();

        Assert.Equal(AiRuntimeRecoveryStatus.Disabled, status);
        Assert.Equal(AiRuntimeRecoveryStatus.Disabled, runtime.PersistenceStatus);
    }

    [Fact]
    public async Task DurableMutation_IsFlushedBeforeSuccessfulToolResult()
    {
        var runtime = RuntimeWithDurableTaskTool();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend);
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, await coordinator.InitializeAsync());
        var session = Session(runtime);

        var result = await runtime.InvokeToolAsync(
            session.SessionId, "aaia.task.create", JsonSerializer.SerializeToElement(new { title = "durable" }));

        Assert.True(result.Success);
        Assert.Equal(1, backend.LastFlushedSequence);
        Assert.Single(runtime.Tasks.List());
    }

    [Fact]
    public async Task Restart_RestoresLastFlushedCheckpointBeforeReady()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var first = Runtime();
        var task = first.Tasks.Create("survives restart");
        await using (var coordinator = Coordinator(first, backend))
        {
            await coordinator.InitializeAsync();
            await coordinator.PersistMutationAsync("test.task.created");
        }
        var second = Runtime();
        await using var restarted = Coordinator(second, backend);

        var status = await restarted.InitializeAsync();

        Assert.Equal(AiRuntimeRecoveryStatus.Ready, status);
        Assert.Equal(task.Id, Assert.Single(second.Tasks.List()).Id);
        Assert.True(backend.LastFlushedSequence >= 2); // Recovery-Checkpoint wurde ebenfalls geflusht.
    }

    [Fact]
    public async Task JournalCheckpointNewerThanSnapshot_IsReplayed()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var first = Runtime();
        await using (var coordinator = Coordinator(first, backend))
        {
            await coordinator.InitializeAsync();
            first.Tasks.Create("snapshot-one");
            await coordinator.PersistMutationAsync("checkpoint-one");
        }

        first.Tasks.Create("journal-two");
        var orchestration = Persistence(first);
        var newer = await orchestration.CaptureAsync(2, DateTime.UtcNow);
        var entry = AiRuntimeStateCodec.CreateJournalEntry(
            2, "newer-operation", AiRuntimePersistenceCoordinator.CheckpointEventType,
            DateTime.UtcNow, false, newer.Payload);
        await using (var writer = await new AiInMemoryRuntimeStateStore(backend)
                         .OpenAsync(AiStateStoreOpenMode.ReadWrite, "crashed-writer"))
        {
            await writer.AppendJournalAsync(entry);
            await writer.FlushAsync();
        }

        var second = Runtime();
        await using var restarted = Coordinator(second, backend);
        await restarted.InitializeAsync();

        Assert.Equal(2, second.Tasks.Count);
        Assert.Contains(second.Tasks.List(), item => item.Title == "journal-two");
    }

    [Fact]
    public async Task UnknownJournalEvent_FailsClosedBeforeRuntimeReady()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using (var writer = await new AiInMemoryRuntimeStateStore(backend)
                         .OpenAsync(AiStateStoreOpenMode.ReadWrite, "writer"))
        {
            var entry = AiRuntimeStateCodec.CreateJournalEntry(
                1, "operation", "unknown.event", DateTime.UtcNow, false, Encoding.UTF8.GetBytes("{}"));
            await writer.AppendJournalAsync(entry);
            await writer.FlushAsync();
        }
        var runtime = Runtime();
        await using var coordinator = Coordinator(runtime, backend);

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await coordinator.InitializeAsync());

        Assert.Equal(AiRuntimeStateReasonCodes.JournalEventUnknown, error.ReasonCode);
        Assert.Equal(AiRuntimeRecoveryStatus.RecoveryFailed, coordinator.Status);
        Assert.Empty(runtime.Tasks.List());
    }

    [Fact]
    public async Task PersistenceFailure_ReturnsErrorInsteadOfSuccessfulMutationConfirmation()
    {
        var runtime = RuntimeWithDurableTaskTool();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var store = new AiInMemoryRuntimeStateStore(backend, maxStoreBytes: 1);
        await using var coordinator = new AiRuntimePersistenceCoordinator(
            runtime, store, new PassthroughProtector(), EnabledOptions(), "runtime");
        await coordinator.InitializeAsync();
        var session = Session(runtime);

        var result = await runtime.InvokeToolAsync(
            session.SessionId, "aaia.task.create", JsonSerializer.SerializeToElement(new { title = "cannot flush" }));

        Assert.False(result.Success);
        Assert.Equal("state_persistence_failed", result.ErrorCode);
        Assert.Equal(AiRuntimeRecoveryStatus.RecoveryFailed, coordinator.Status);
    }

    [Fact]
    public async Task BudgetAndReservationMutations_AreFlushedAndRecovered()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var first = Runtime();
        await using (var coordinator = Coordinator(first, backend))
        {
            await coordinator.InitializeAsync();
            first.Resources.Registry.Register(new AiResourceProfile
            {
                ResourceId = "resource",
                ProviderId = "host",
                Kind = AiResourceKind.Inference,
                Capacity = new AiResourceCapacity { MaxConcurrentExecutions = 1 },
                CostRate = new AiResourceCostRate { CostUnit = "EUR", FixedPerExecution = 2 }
            });
            first.Resources.Registry.UpdateTelemetry(new AiResourceTelemetry
            {
                ResourceId = "resource",
                ObservedAtUtc = DateTime.UtcNow
            });
            first.Resources.SetBudget(new AiResourceBudget
            {
                Id = "budget",
                Scope = AiBudgetScope.Runtime,
                CostUnit = "EUR",
                Window = AiBudgetWindow.Day,
                HardLimit = 10,
                WindowStartsAtUtc = DateTime.UtcNow.AddHours(-1),
                WindowEndsAtUtc = DateTime.UtcNow.AddHours(23)
            });
            var decision = first.Resources.SelectAndReserve(new AiResourceRequest
            {
                ExecutionRequestId = "execution",
                TaskId = "task",
                SessionId = "session",
                Requirements = new AiResourceRequirements
                {
                    Kind = AiResourceKind.Inference,
                    CostUnit = "EUR"
                }
            });
            Assert.Equal(AiResourceDecisionStatus.Selected, decision.Status);
            Assert.Equal(2, backend.LastFlushedSequence);
        }

        var second = Runtime();
        await using var restarted = Coordinator(second, backend);
        await restarted.InitializeAsync();

        Assert.Equal(0, Assert.Single(second.Resources.ListBudgets()).Reserved);
        var reservation = Assert.Single(second.Resources.ListReservations());
        Assert.Equal(AiReservationState.Released, reservation.State);
        Assert.Equal(AiResourceReasonCodes.RuntimeRecovery, reservation.SettlementReasonCode);
    }

    [Fact]
    public async Task ExclusiveMaintenance_TemporarilyReleasesWriterAndReturnsReady()
    {
        var runtime = Runtime();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var store = new AiInMemoryRuntimeStateStore(backend);
        await using var coordinator = new AiRuntimePersistenceCoordinator(
            runtime, store, new PassthroughProtector(), EnabledOptions(), "runtime");
        await coordinator.InitializeAsync();
        var maintenance = new AiRuntimeStateMaintenanceService(
            store, runtime.Audit, new AllowMaintenance(), "maintenance");

        var result = await coordinator.RunExclusiveMaintenanceAsync(
            ct => maintenance.BackupAsync("owner", "checkpoint", true, ct));

        Assert.True(result.Success);
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, coordinator.Status);
        Assert.Equal(1, backend.LastFlushedSequence); // Wartungs-Audit wurde checkpointed.
    }

    [Fact]
    public async Task TypedDeltaWriter_AppendsTypedMutationInsteadOfPhase9Checkpoint()
    {
        var runtime = RuntimeWithDurableTaskTool();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend, options: TypedOptions());
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, await coordinator.InitializeAsync());
        var session = Session(runtime);

        var result = await runtime.InvokeToolAsync(
            session.SessionId, "aaia.task.create", JsonSerializer.SerializeToElement(new { title = "delta" }));

        Assert.True(result.Success);
        var entries = await ReadJournal(backend);
        Assert.Contains(entries, entry => entry.EventType == "air.mutation.task.created");
        Assert.DoesNotContain(entries,
            entry => entry.EventType == AiRuntimePersistenceCoordinator.CheckpointEventType);
    }

    [Fact]
    public async Task TypedDeltaWriter_MigratesPhase9SnapshotAndRecoversMixedDeltaJournal()
    {
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var legacy = Runtime();
        var legacyTask = legacy.Tasks.Create("legacy");
        await using (var coordinator = Coordinator(legacy, backend))
        {
            await coordinator.InitializeAsync();
            await coordinator.PersistMutationAsync("legacy.task.created");
        }

        var migrated = RuntimeWithDurableTaskTool();
        await using (var coordinator = Coordinator(migrated, backend, options: TypedOptions()))
        {
            Assert.Equal(AiRuntimeRecoveryStatus.Ready, await coordinator.InitializeAsync());
            Assert.Equal(legacyTask.Id, Assert.Single(migrated.Tasks.List()).Id);
            var session = Session(migrated);
            var result = await migrated.InvokeToolAsync(
                session.SessionId, "aaia.task.create", JsonSerializer.SerializeToElement(new { title = "new" }));
            Assert.True(result.Success);
        }

        var restarted = Runtime();
        await using var recovery = Coordinator(restarted, backend, options: TypedOptions());
        Assert.Equal(AiRuntimeRecoveryStatus.Ready, await recovery.InitializeAsync());

        Assert.Equal(new[] { "legacy", "new" },
            restarted.Tasks.List().Select(task => task.Title).OrderBy(title => title).ToArray());
        var manifest = await ReadManifest(backend);
        Assert.True(manifest!.FeatureFlags["typedDeltaJournal"]);
    }

    [Fact]
    public async Task RollbackSwitch_ForcesPhase9CheckpointWriter()
    {
        var runtime = RuntimeWithDurableTaskTool();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend, options: TypedOptions(rollback: true));
        await coordinator.InitializeAsync();
        var session = Session(runtime);

        var result = await runtime.InvokeToolAsync(
            session.SessionId, "aaia.task.create", JsonSerializer.SerializeToElement(new { title = "rollback" }));

        Assert.True(result.Success);
        Assert.Contains(await ReadJournal(backend),
            entry => entry.EventType == AiRuntimePersistenceCoordinator.CheckpointEventType);
    }

    private static AiRuntimePersistenceCoordinator Coordinator(
        AiRuntimeService runtime,
        AiInMemoryRuntimeStateStoreBackend backend,
        bool enabled = true,
        AiRuntimePersistenceOptions? options = null)
        => new(runtime, new AiInMemoryRuntimeStateStore(backend), new PassthroughProtector(),
            options ?? (enabled ? EnabledOptions() : new AiRuntimePersistenceOptions()), "runtime");

    private static AiRuntimePersistenceOptions EnabledOptions() => new() { Enabled = true };

    private static AiRuntimePersistenceOptions TypedOptions(bool rollback = false) => new()
    {
        Enabled = true,
        UseTypedDeltaWriter = true,
        RollbackToPhase9CheckpointWriter = rollback
    };

    private static AiOrchestrationPersistenceService Persistence(AiRuntimeService runtime)
        => new(runtime, new PassthroughProtector(), "store");

    private static AiRuntimeService Runtime() => new(
        new AiToolRegistry(), new AiSessionManager(), new AiCapabilityManager(),
        new AiPermissionEngine(), new AiWorkspaceLockService(),
        new AiRuntimeEventBus(), new AiAuditService());

    private static AiRuntimeService RuntimeWithDurableTaskTool()
    {
        var runtime = Runtime();
        runtime.Tools.Register(new AiToolDefinition
        {
            Name = "aaia.task.create",
            Description = "test",
            RiskLevel = AiRiskLevel.Green,
            RequiredPermissions = AiPermission.Read,
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
            Handler = (invocation, _) =>
            {
                var title = invocation.Input.GetProperty("title").GetString()!;
                var task = runtime.Tasks.Create(title);
                return Task.FromResult(AiToolResult.Ok(new { taskId = task.Id }));
            }
        });
        return runtime;
    }

    private static AiSession Session(AiRuntimeService runtime)
        => runtime.Sessions.Create(
            new AiClientIdentity { Name = "client", Fingerprint = "client" },
            grantedPermissions: AiPermission.Read);

    private static AiStateProtectionContext Context(string store, string type, string id) => new()
    {
        StoreId = store,
        RecordType = type,
        RecordId = id
    };

    private static async Task<IReadOnlyList<AiRuntimeJournalEntry>> ReadJournal(
        AiInMemoryRuntimeStateStoreBackend backend)
    {
        await using var reader = await new AiInMemoryRuntimeStateStore(backend)
            .OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");
        var entries = new List<AiRuntimeJournalEntry>();
        await foreach (var entry in reader.ReadJournalAsync(0)) entries.Add(entry);
        return entries;
    }

    private static async Task<AiRuntimeStateManifest?> ReadManifest(
        AiInMemoryRuntimeStateStoreBackend backend)
    {
        await using var reader = await new AiInMemoryRuntimeStateStore(backend)
            .OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");
        return await reader.LoadManifestAsync();
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

    private sealed class AllowMaintenance : IAiStateMaintenanceAuthorizer
    {
        public bool IsAuthorized(string actorId, string action, bool confirmed, out string? denialReason)
        {
            denialReason = null;
            return confirmed;
        }
    }
}
