using System.Text;
using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9DurableResourceTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task OpenReservation_IsReleasedExactlyOnceAndReservedBudgetIsFreed()
    {
        var source = Runtime();
        ConfigureResource(source, "session-before-crash");
        var selected = source.Resources.SelectAndReserve(Request("execution-open", "session-before-crash"));
        Assert.Equal(AiResourceDecisionStatus.Selected, selected.Status);
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();

        var report = await Service(target).RestoreAsync(snapshot);

        Assert.Equal(1, report.RecoveryReleasedReservations);
        var budget = Assert.Single(target.Resources.ListBudgets());
        Assert.Equal(0, budget.Reserved);
        Assert.Equal(0, budget.Spent);
        var reservation = Assert.Single(target.Resources.ListReservations());
        Assert.Equal(AiReservationState.Released, reservation.State);
        Assert.Equal(AiResourceReasonCodes.RuntimeRecovery, reservation.SettlementReasonCode);
        Assert.Null(reservation.SessionId);

        var secondSnapshot = await Service(target).CaptureAsync(2, Now.AddSeconds(1));
        var secondTarget = Runtime();
        var secondReport = await Service(secondTarget).RestoreAsync(secondSnapshot);
        Assert.Equal(0, secondReport.RecoveryReleasedReservations);
        Assert.Equal(0, Assert.Single(secondTarget.Resources.ListBudgets()).Reserved);
    }

    [Fact]
    public async Task CommittedReservation_ReconstructsSpentWithoutDoubleCharge()
    {
        var source = Runtime();
        ConfigureResource(source, "session");
        var selected = source.Resources.SelectAndReserve(Request("execution-commit", "session"));
        Assert.True(source.Resources.Commit(selected.Reservation!.Id, 3.25m, out _));
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();

        await Service(target).RestoreAsync(snapshot);

        var budget = Assert.Single(target.Resources.ListBudgets());
        Assert.Equal(3.25m, budget.Spent);
        Assert.Equal(0, budget.Reserved);
        var reservation = Assert.Single(target.Resources.ListReservations());
        Assert.Equal(AiReservationState.Committed, reservation.State);
        Assert.Equal(3.25m, reservation.ActualCost);
        Assert.True(target.Resources.Commit(reservation.Id, 3.25m, out var error));
        Assert.Null(error);
        Assert.Equal(3.25m, Assert.Single(target.Resources.ListBudgets()).Spent);
    }

    [Fact]
    public async Task ProfilesSessionsAndTelemetry_AreNotRestored()
    {
        var source = Runtime();
        ConfigureResource(source, "transient-session");
        _ = source.Resources.SelectAndReserve(Request("execution", "transient-session"));
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var json = Encoding.UTF8.GetString(snapshot.Payload);
        var target = Runtime();

        await Service(target).RestoreAsync(snapshot);

        Assert.Empty(target.Resources.Registry.ListProfiles());
        Assert.Null(target.Resources.Registry.GetTelemetry("resource-1"));
        Assert.DoesNotContain("transient-session", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idempotency_ReplaysStableResultIdAfterRestartWithoutPayloadOrFactory()
    {
        var source = Runtime();
        var first = source.Idempotency.Execute(
            "stable-client", "execution.enqueue", "request-1", "PRIVATE_REQUEST_PAYLOAD",
            () => AiToolResult.Ok(new { executionId = "execution-result-1", detail = "PRIVATE_RESULT_PAYLOAD" }),
            result => result.Payload.GetProperty("executionId").GetString());
        Assert.True(first.Success);
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var persistedJson = Encoding.UTF8.GetString(snapshot.Payload);
        var target = Runtime();
        await Service(target).RestoreAsync(snapshot);
        var factoryCalls = 0;

        var replay = target.Idempotency.Execute(
            "stable-client", "execution.enqueue", "request-1", "PRIVATE_REQUEST_PAYLOAD",
            () =>
            {
                Interlocked.Increment(ref factoryCalls);
                return AiToolResult.Ok(new { executionId = "wrong" });
            },
            result => result.Payload.GetProperty("executionId").GetString(),
            resultId => AiToolResult.Ok(new { executionId = resultId, replayed = true }));

        Assert.Equal(0, factoryCalls);
        Assert.Equal("execution-result-1", replay.Payload.GetProperty("executionId").GetString());
        Assert.True(replay.Payload.GetProperty("replayed").GetBoolean());
        Assert.DoesNotContain("PRIVATE_REQUEST_PAYLOAD", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_RESULT_PAYLOAD", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idempotency_DifferentInputAfterRestartRemainsConflict()
    {
        var source = Runtime();
        _ = source.Idempotency.Execute(
            "client", "op", "same-id", "input-a",
            () => AiToolResult.Ok(new { resultId = "result-a" }),
            result => result.Payload.GetProperty("resultId").GetString());
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();
        await Service(target).RestoreAsync(snapshot);

        var conflict = target.Idempotency.Execute(
            "client", "op", "same-id", "input-b",
            () => AiToolResult.Ok(new { resultId = "result-b" }));

        Assert.False(conflict.Success);
        Assert.Equal(AiPhase8ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
    }

    [Fact]
    public async Task ExpiredIdempotencyRecord_IsNotRestored()
    {
        var source = Runtime();
        _ = source.Idempotency.Execute(
            "client", "op", "expires", "input",
            () => AiToolResult.Ok(new { resultId = "result" }),
            result => result.Payload.GetProperty("resultId").GetString());
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();
        var future = new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(25));

        var report = await Service(target, future).RestoreAsync(snapshot);

        Assert.Equal(0, report.IdempotencyRecordCount);
        Assert.Equal(0, target.Idempotency.Count);
    }

    [Fact]
    public async Task IdempotencyCapacity_KeepsNewestTenThousandDeterministically()
    {
        var source = Runtime();
        for (var index = 0; index < 10_001; index++)
        {
            var value = index.ToString("D5");
            _ = source.Idempotency.Execute(
                "client", "op", $"id-{value}", $"input-{value}",
                () => AiToolResult.Ok(new { resultId = $"result-{value}" }),
                result => result.Payload.GetProperty("resultId").GetString());
        }

        var snapshot = await Service(source).CaptureAsync(1, Now, 8 * 1024 * 1024);
        var durable = JsonSerializer.Deserialize<AiDurableOrchestrationSnapshot>(snapshot.Payload)!;

        Assert.Equal(10_000, durable.IdempotencyRecords.Count);
        Assert.DoesNotContain(durable.IdempotencyRecords, record => record.IdempotencyId == "id-00000");
        Assert.Contains(durable.IdempotencyRecords, record => record.IdempotencyId == "id-10000");
    }

    [Fact]
    public async Task InconsistentBudgetSnapshot_IsRejectedAndRuntimeIsRolledBack()
    {
        var source = Runtime();
        ConfigureResource(source, "session");
        _ = source.Resources.SelectAndReserve(Request("execution", "session"));
        var original = await Service(source).CaptureAsync(1, Now);
        var durable = JsonSerializer.Deserialize<AiDurableOrchestrationSnapshot>(original.Payload)!;
        var budget = Assert.Single(durable.Budgets);
        var corrupt = new AiDurableOrchestrationSnapshot
        {
            CreatedAtUtc = durable.CreatedAtUtc,
            Tasks = durable.Tasks,
            Executions = durable.Executions,
            Budgets = new[]
            {
                new AiDurableBudgetSnapshot
                {
                    Budget = budget.Budget,
                    Spent = budget.Spent,
                    Reserved = budget.Reserved + 1
                }
            },
            Reservations = durable.Reservations,
            IdempotencyRecords = durable.IdempotencyRecords
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(corrupt);
        var snapshot = AiRuntimeStateCodec.CreateSnapshot(1, Now, payload);
        var target = Runtime();

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await Service(target).RestoreAsync(snapshot));

        Assert.Equal(AiRuntimeStateReasonCodes.SnapshotCorrupt, error.ReasonCode);
        Assert.Empty(target.Resources.ListBudgets());
        Assert.Empty(target.Resources.ListReservations());
        Assert.Equal(0, target.Tasks.Count);
        Assert.Empty(target.Scheduler.List());
    }

    private static void ConfigureResource(AiRuntimeService runtime, string sessionId)
    {
        runtime.Resources.Registry.Register(new AiResourceProfile
        {
            ResourceId = "resource-1",
            ProviderId = "provider",
            Kind = AiResourceKind.Inference,
            Capacity = new AiResourceCapacity { MaxConcurrentExecutions = 4 },
            CostRate = new AiResourceCostRate { CostUnit = "EUR", FixedPerExecution = 2 }
        });
        runtime.Resources.Registry.UpdateTelemetry(new AiResourceTelemetry
        {
            ResourceId = "resource-1",
            ObservedAtUtc = DateTime.UtcNow,
            Healthy = true
        });
        runtime.Resources.SetBudget(new AiResourceBudget
        {
            Id = "budget-1",
            Scope = AiBudgetScope.Runtime,
            CostUnit = "EUR",
            Window = AiBudgetWindow.Day,
            HardLimit = 100,
            WindowStartsAtUtc = DateTime.UtcNow.AddHours(-1),
            WindowEndsAtUtc = DateTime.UtcNow.AddHours(23)
        });
        _ = sessionId;
    }

    private static AiResourceRequest Request(string executionId, string sessionId) => new()
    {
        ExecutionRequestId = executionId,
        TaskId = $"task-{executionId}",
        SessionId = sessionId,
        Requirements = new AiResourceRequirements
        {
            Kind = AiResourceKind.Inference,
            CostUnit = "EUR",
            ReservationDuration = TimeSpan.FromMinutes(5)
        }
    };

    private static AiRuntimeService Runtime() => new(
        new AiToolRegistry(),
        new AiSessionManager(),
        new AiCapabilityManager(),
        new AiPermissionEngine(),
        new AiWorkspaceLockService(),
        new AiRuntimeEventBus(),
        new AiAuditService());

    private static AiOrchestrationPersistenceService Service(
        AiRuntimeService runtime,
        TimeProvider? timeProvider = null)
        => new(runtime, new PassthroughProtector(), "store", timeProvider: timeProvider);

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
