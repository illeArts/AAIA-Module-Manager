using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Resources;
using AAIA.Air.Scheduling;
using AAIA.Air.Tasks;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase8ResourceIntegrationTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    [Fact]
    public async Task SelectedResource_RunsThroughTaskManagerAndCommitsReservation()
    {
        var c = CreateContext();
        Register(c, "resource", new[] { "build" });
        var calls = 0;
        c.Tasks.Executor = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult((true, "{}"));
        };
        var task = CreateTask(c.Tasks);
        var execution = c.Scheduler.Enqueue(task.Id,
            resourceRequirements: Requirements(new[] { "build" }));
        Assert.True(c.Scheduler.TryAssignNext(out var lease));

        var result = await c.Scheduler.RunAsync(execution.Request.Id, lease!.SessionId);

        Assert.Equal(AiExecutionState.Completed, result.State);
        Assert.Equal(1, calls);
        Assert.Equal("resource", result.ResourceId);
        Assert.Equal(AiReservationState.Committed,
            c.Resources.GetReservation(result.ResourceReservationId!)!.State);
    }

    [Fact]
    public async Task NonRetryableDenial_FailsWithoutToolExecution()
    {
        var c = CreateContext();
        Register(c, "resource", new[] { "files" });
        var calls = 0;
        c.Tasks.Executor = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult((true, "{}"));
        };
        var task = CreateTask(c.Tasks);
        var execution = c.Scheduler.Enqueue(task.Id,
            resourceRequirements: Requirements(new[] { "vision" }));
        Assert.True(c.Scheduler.TryAssignNext(out var lease));

        var result = await c.Scheduler.RunAsync(execution.Request.Id, lease!.SessionId);

        Assert.Equal(AiExecutionState.Failed, result.State);
        Assert.Equal(AiResourceReasonCodes.NoMatchingResource, result.LastError);
        Assert.Equal(0, calls);
        Assert.Equal(AiTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task RetryableCapacityDenial_RequeuesWithoutConsumingMaxAttempts()
    {
        var c = CreateContext();
        Register(c, "resource", new[] { "build" }, slots: 1);
        var occupied = c.Resources.SelectAndReserve(ResourceRequest("occupied", "other", Requirements(new[] { "build" })));
        Assert.Equal(AiResourceDecisionStatus.Selected, occupied.Status);
        var task = CreateTask(c.Tasks);
        var execution = c.Scheduler.Enqueue(task.Id, maxAttempts: 1,
            resourceRequirements: Requirements(new[] { "build" }));
        Assert.True(c.Scheduler.TryAssignNext(out var lease));

        var result = await c.Scheduler.RunAsync(execution.Request.Id, lease!.SessionId);

        Assert.Equal(AiExecutionState.Queued, result.State);
        Assert.Equal(0, result.AttemptCount);
        Assert.Equal(1, result.ResourceDeferralCount);
        Assert.Equal(AiTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task CancelledExecution_LeavesNoReservedResource()
    {
        var c = CreateContext();
        Register(c, "resource", new[] { "build" });
        c.Tasks.Executor = async (_, _, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return (true, "{}");
        };
        var task = CreateTask(c.Tasks);
        var execution = c.Scheduler.Enqueue(task.Id,
            resourceRequirements: Requirements(new[] { "build" }));
        Assert.True(c.Scheduler.TryAssignNext(out var lease));
        var running = c.Scheduler.RunAsync(execution.Request.Id, lease!.SessionId);
        Assert.True(SpinWait.SpinUntil(() => task.Status == AiTaskStatus.InProgress, TimeSpan.FromSeconds(2)));

        Assert.True(c.Scheduler.Cancel(execution.Request.Id));
        var result = await running;

        Assert.Equal(AiExecutionState.Cancelled, result.State);
        Assert.DoesNotContain(c.Resources.ListReservations(), r => r.State == AiReservationState.Reserved);
    }

    [Fact]
    public async Task ResourceSelection_DoesNotChangeOwnerOrSchedulerPriority()
    {
        var c = CreateContext();
        Register(c, "resource", new[] { "build" });
        c.Tasks.Executor = (_, _, _, _) => Task.FromResult((true, "{}"));
        var task = CreateTask(c.Tasks);
        var execution = c.Scheduler.Enqueue(task.Id, AiExecutionPriority.Critical,
            resourceRequirements: Requirements(new[] { "build" }));
        Assert.True(c.Scheduler.TryAssignNext(out var lease));
        var ownerBefore = task.OwnerSessionId;

        var result = await c.Scheduler.RunAsync(execution.Request.Id, lease!.SessionId);

        Assert.Equal(ownerBefore, task.OwnerSessionId);
        Assert.Equal(AiExecutionPriority.Critical, result.Request.Priority);
    }

    private static IntegrationContext CreateContext()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 6, 23, 18, 0, 0, TimeSpan.Zero));
        var tasks = new AiTaskManager();
        var sessions = new AiSessionManager();
        var events = new AiRuntimeEventBus();
        var resources = new AiResourceManager(time, events: events);
        var scheduler = new AiExecutionScheduler(tasks, sessions, events, time,
            resources: resources, maxResourceDeferrals: 3);
        var session = sessions.Create(new AiClientIdentity { Name = "developer", Fingerprint = "developer" });
        return new IntegrationContext(tasks, scheduler, resources, session, time);
    }

    private static void Register(IntegrationContext c, string id, string[] capabilities, int slots = 2)
    {
        c.Resources.Registry.Register(new AiResourceProfile
        {
            ResourceId = id,
            ProviderId = "test",
            Kind = AiResourceKind.Inference,
            Capabilities = capabilities,
            Capacity = new AiResourceCapacity
            {
                MaxConcurrentExecutions = slots,
                ContextWindowTokens = 100_000,
                RequestsPerMinute = 100,
                TokensPerMinute = 1_000_000
            },
            CostRate = new AiResourceCostRate { CostUnit = "EUR", FixedPerExecution = 1 }
        });
        c.Resources.Registry.UpdateTelemetry(new AiResourceTelemetry
        {
            ResourceId = id,
            ObservedAtUtc = c.Time.UtcNow.UtcDateTime,
            Healthy = true,
            P95ExecutionLatencyMs = 100
        });
    }

    private static AiTask CreateTask(AiTaskManager tasks)
        => tasks.Create("resource task", project: "project", steps: new[]
        {
            new AiTaskStep { ToolName = "aaia.test", Input = Empty }
        });

    private static AiResourceRequirements Requirements(string[] capabilities)
        => new()
        {
            Kind = AiResourceKind.Inference,
            RequiredCapabilities = capabilities,
            CostUnit = "EUR",
            ReservationDuration = TimeSpan.FromMinutes(5)
        };

    private static AiResourceRequest ResourceRequest(
        string executionId, string taskId, AiResourceRequirements requirements)
        => new()
        {
            ExecutionRequestId = executionId,
            TaskId = taskId,
            ProjectId = "project",
            SessionId = "session",
            Requirements = requirements
        };

    private sealed record IntegrationContext(
        AiTaskManager Tasks,
        AiExecutionScheduler Scheduler,
        AiResourceManager Resources,
        AiSession Session,
        ManualTimeProvider Time);

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
