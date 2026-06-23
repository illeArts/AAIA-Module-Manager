using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Scheduling;
using AAIA.Air.Tasks;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase8SchedulerTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    [Fact]
    public void HigherPriority_IsLeasedFirst()
    {
        var context = CreateScheduler();
        var session = CreateSession(context.Sessions, "developer");
        var low = context.Tasks.Create("Low");
        var high = context.Tasks.Create("High");
        context.Scheduler.Enqueue(low.Id, AiExecutionPriority.Low);
        context.Scheduler.Enqueue(high.Id, AiExecutionPriority.High);

        Assert.True(context.Scheduler.TryAssignNext(out var lease));
        Assert.Equal(high.Id, lease!.TaskId);
        Assert.Equal(session.SessionId, lease.SessionId);
    }

    [Fact]
    public void RoleAndCapabilities_AreHardAssignmentFilters()
    {
        var context = CreateScheduler();
        CreateSession(context.Sessions, "reader");
        var developer = CreateSession(context.Sessions, "developer", AiRole.Developer, AiCapabilities.Terminal);
        var task = context.Tasks.Create("Build");
        context.Scheduler.Enqueue(task.Id, requiredRole: AiRole.Developer,
            requiredCapabilities: new[] { AiCapabilities.Terminal });

        Assert.True(context.Scheduler.TryAssignNext(out var lease));
        Assert.Equal(developer.SessionId, lease!.SessionId);
    }

    [Fact]
    public void OneActiveLeasePerSession_DistributesWork()
    {
        var context = CreateScheduler();
        var first = CreateSession(context.Sessions, "first");
        var second = CreateSession(context.Sessions, "second");
        context.Scheduler.Enqueue(context.Tasks.Create("One").Id);
        context.Scheduler.Enqueue(context.Tasks.Create("Two").Id);

        Assert.True(context.Scheduler.TryAssignNext(out var firstLease));
        Assert.True(context.Scheduler.TryAssignNext(out var secondLease));
        Assert.NotEqual(firstLease!.SessionId, secondLease!.SessionId);
        Assert.Contains(firstLease.SessionId, new[] { first.SessionId, second.SessionId });
    }

    [Fact]
    public void Aging_PreventsLowPriorityStarvation()
    {
        var context = CreateScheduler(agingInterval: TimeSpan.FromMinutes(5));
        CreateSession(context.Sessions, "developer");
        var oldLow = context.Tasks.Create("Old low");
        context.Scheduler.Enqueue(oldLow.Id, AiExecutionPriority.Low);
        context.Time.Advance(TimeSpan.FromMinutes(15));
        var newHigh = context.Tasks.Create("New high");
        context.Scheduler.Enqueue(newHigh.Id, AiExecutionPriority.High);

        Assert.True(context.Scheduler.TryAssignNext(out var lease));
        Assert.Equal(oldLow.Id, lease!.TaskId);
    }

    [Fact]
    public void ExpiredLease_RequeuesThenFailsAtMaxAttempts()
    {
        var context = CreateScheduler(leaseDuration: TimeSpan.FromMinutes(1));
        CreateSession(context.Sessions, "developer");
        var task = context.Tasks.Create("Retry");
        var request = context.Scheduler.Enqueue(task.Id, maxAttempts: 2);

        Assert.True(context.Scheduler.TryAssignNext(out _));
        context.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, context.Scheduler.RecoverExpiredLeases());
        Assert.Equal(AiExecutionState.Queued, context.Scheduler.Get(request.Request.Id)!.State);
        Assert.Equal(AiTaskStatus.Pending, task.Status);

        Assert.True(context.Scheduler.TryAssignNext(out _));
        context.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, context.Scheduler.RecoverExpiredLeases());
        Assert.Equal(AiExecutionState.Failed, context.Scheduler.Get(request.Request.Id)!.State);
        Assert.Equal(AiTaskStatus.Pending, task.Status);
    }

    [Fact]
    public void NotBefore_BlocksEarlyAssignment()
    {
        var context = CreateScheduler();
        CreateSession(context.Sessions, "developer");
        var task = context.Tasks.Create("Later");
        context.Scheduler.Enqueue(task.Id, notBeforeUtc: context.Time.GetUtcNow().UtcDateTime.AddMinutes(5));

        Assert.False(context.Scheduler.TryAssignNext(out _));
        context.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(context.Scheduler.TryAssignNext(out _));
    }

    [Fact]
    public async Task RunAsync_DelegatesToTaskManagerAndCompletes()
    {
        var context = CreateScheduler();
        var session = CreateSession(context.Sessions, "developer");
        context.Tasks.Executor = (_, _, _, _) => Task.FromResult((true, "{}"));
        var task = context.Tasks.Create("Run", steps: new[]
        {
            new AiTaskStep { ToolName = "aaia.test", Input = Empty }
        });
        var request = context.Scheduler.Enqueue(task.Id);
        Assert.True(context.Scheduler.TryAssignNext(out _));

        var result = await context.Scheduler.RunAsync(request.Request.Id, session.SessionId);

        Assert.Equal(AiExecutionState.Completed, result.State);
        Assert.Equal(AiTaskStatus.Completed, task.Status);
    }

    [Fact]
    public async Task CancelRunningExecution_LeavesNoInProgressState()
    {
        var context = CreateScheduler();
        var session = CreateSession(context.Sessions, "developer");
        context.Tasks.Executor = async (_, _, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return (true, "{}");
        };
        var task = context.Tasks.Create("Cancel", steps: new[]
        {
            new AiTaskStep { ToolName = "aaia.wait", Input = Empty }
        });
        var request = context.Scheduler.Enqueue(task.Id);
        Assert.True(context.Scheduler.TryAssignNext(out _));

        var running = context.Scheduler.RunAsync(request.Request.Id, session.SessionId);
        Assert.True(SpinWait.SpinUntil(() => task.Status == AiTaskStatus.InProgress, TimeSpan.FromSeconds(2)));
        Assert.True(context.Scheduler.Cancel(request.Request.Id));
        var result = await running;

        Assert.Equal(AiExecutionState.Cancelled, result.State);
        Assert.Equal(AiTaskStatus.Cancelled, task.Status);
        Assert.DoesNotContain(task.Steps, step => step.Status == AiTaskStepStatus.Running);
    }

    [Fact]
    public void ConcurrentAssignment_ProducesOnlyOneLeasePerTask()
    {
        var context = CreateScheduler();
        CreateSession(context.Sessions, "first");
        CreateSession(context.Sessions, "second");
        context.Scheduler.Enqueue(context.Tasks.Create("Single").Id);
        var leases = new System.Collections.Concurrent.ConcurrentBag<AiExecutionLease>();

        Parallel.For(0, 20, _ =>
        {
            if (context.Scheduler.TryAssignNext(out var lease)) leases.Add(lease!);
        });

        Assert.Single(leases);
    }

    [Fact]
    public async Task ExecutorFailure_MarksTaskAndExecutionFailed()
    {
        var context = CreateScheduler();
        var session = CreateSession(context.Sessions, "developer");
        context.Tasks.Executor = (_, _, _, _) => throw new InvalidOperationException("boom");
        var task = context.Tasks.Create("Fail", steps: new[]
        {
            new AiTaskStep { ToolName = "aaia.fail", Input = Empty }
        });
        var request = context.Scheduler.Enqueue(task.Id);
        Assert.True(context.Scheduler.TryAssignNext(out _));

        var result = await context.Scheduler.RunAsync(request.Request.Id, session.SessionId);

        Assert.Equal(AiExecutionState.Failed, result.State);
        Assert.Equal(AiTaskStatus.Failed, task.Status);
        Assert.Equal(AiTaskStepStatus.Failed, Assert.Single(task.Steps).Status);
    }

    private static SchedulerContext CreateScheduler(
        TimeSpan? leaseDuration = null,
        TimeSpan? agingInterval = null)
    {
        var tasks = new AiTaskManager();
        var sessions = new AiSessionManager();
        var events = new AiRuntimeEventBus();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));
        var scheduler = new AiExecutionScheduler(tasks, sessions, events, time, leaseDuration, agingInterval);
        return new SchedulerContext(tasks, sessions, scheduler, time);
    }

    private static AiSession CreateSession(
        AiSessionManager sessions,
        string name,
        AiRole? role = null,
        params string[] capabilities)
    {
        var session = sessions.Create(new AiClientIdentity { Name = name, Fingerprint = name }, capabilities);
        if (role.HasValue) session.Roles.Add(role.Value);
        return session;
    }

    private sealed record SchedulerContext(
        AiTaskManager Tasks,
        AiSessionManager Sessions,
        AiExecutionScheduler Scheduler,
        ManualTimeProvider Time);

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _utcNow = initial;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
