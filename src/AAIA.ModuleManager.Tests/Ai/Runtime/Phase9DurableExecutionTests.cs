using System.Text;
using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase9DurableExecutionTests
{
    private static readonly DateTime Now = new(2026, 6, 24, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecoveryRequired_IsExplicitNonTerminalState()
    {
        Assert.True(Enum.IsDefined(AiTaskStatus.RecoveryRequired));
        Assert.True(Enum.IsDefined(AiExecutionState.RecoveryRequired));
    }

    [Fact]
    public void DurableContracts_ContainNoSessionLeaseOrHandlerProperties()
    {
        var taskProperties = typeof(AiDurableTaskSnapshot).GetProperties().Select(property => property.Name).ToArray();
        var executionProperties = typeof(AiDurableExecutionSnapshot).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(taskProperties, name => name.Contains("Session", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(executionProperties, name => name.Contains("Session", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(executionProperties, name => name.Contains("Lease", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(taskProperties, name => name.Contains("Handler", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingProtector_IsRejected()
    {
        var error = Assert.Throws<AiStateStoreException>(() =>
            new AiOrchestrationPersistenceService(Runtime(), null!, "store"));

        Assert.Equal(AiRuntimeStateReasonCodes.ProtectorUnavailable, error.ReasonCode);
    }

    [Fact]
    public async Task Capture_IsDeterministicAndContainsNoPlaintextInputOrResult()
    {
        var runtime = Runtime();
        var input = JsonSerializer.SerializeToElement(new { instruction = "compile-project" });
        var task = runtime.Tasks.Create("Build", steps: new[]
        {
            new AiTaskStep
            {
                ToolName = "aaia.project.build",
                Input = input,
                ResultJson = "sensitive-result-must-not-persist"
            }
        });
        runtime.Scheduler.Enqueue(task.Id, submittedByClientId: "client-stable");
        var service = Service(runtime);

        var first = await service.CaptureAsync(1, Now);
        var second = await service.CaptureAsync(1, Now);
        var json = Encoding.UTF8.GetString(first.Payload);

        Assert.Equal(first.ChecksumSha256, second.ChecksumSha256);
        Assert.Equal(first.Payload, second.Payload);
        Assert.DoesNotContain("compile-project", json);
        Assert.DoesNotContain("sensitive-result-must-not-persist", json);
        Assert.Contains("client-stable", json);
    }

    [Fact]
    public async Task SensitiveTaskInput_IsRejectedBeforeProtector()
    {
        var runtime = Runtime();
        runtime.Tasks.Create("Unsafe", steps: new[]
        {
            new AiTaskStep
            {
                ToolName = "aaia.test",
                Input = JsonSerializer.SerializeToElement(new
                {
                    value = "Bearer abcdefghijklmnopqrstuvwxyz123456"
                })
            }
        });

        var error = await Assert.ThrowsAsync<AiStateStoreException>(async () =>
            await Service(runtime).CaptureAsync(1, Now));

        Assert.Equal(AiRuntimeStateReasonCodes.PayloadRejected, error.ReasonCode);
    }

    [Fact]
    public async Task QueuedTaskAndExecution_RoundTripThroughStateStore()
    {
        var source = Runtime();
        var task = source.Tasks.Create("Queued", project: "project", steps: new[]
        {
            Step("aaia.test", new { value = 42 })
        });
        var execution = source.Scheduler.Enqueue(
            task.Id, AiExecutionPriority.High,
            requiredCapabilities: new[] { AiCapabilities.Files },
            maxAttempts: 5,
            submittedBySessionId: "ephemeral-session",
            submittedByClientId: "stable-client");
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        var store = new AiInMemoryRuntimeStateStore(backend);
        await using (var writer = await store.OpenAsync(AiStateStoreOpenMode.ReadWrite, "writer"))
        {
            await writer.AppendJournalAsync(AiRuntimeStateCodec.CreateJournalEntry(
                1, "snapshot-1", "orchestration.snapshot", Now, false, Array.Empty<byte>()));
            await writer.WriteSnapshotAsync(snapshot);
        }
        await using var reader = await store.OpenAsync(AiStateStoreOpenMode.ReadOnly, "reader");
        var persisted = await reader.LoadSnapshotAsync();
        var target = Runtime();

        var report = await Service(target).RestoreAsync(persisted!);

        Assert.Equal(1, report.TaskCount);
        Assert.Equal(1, report.ExecutionCount);
        var restoredTask = target.Tasks.Get(task.Id)!;
        Assert.Equal(AiTaskStatus.Pending, restoredTask.Status);
        Assert.Null(restoredTask.OwnerSessionId);
        Assert.Equal(42, restoredTask.Steps[0].Input.GetProperty("value").GetInt32());
        Assert.Null(restoredTask.Steps[0].ResultJson);
        var restoredExecution = target.Scheduler.Get(execution.Request.Id)!;
        Assert.Equal(AiExecutionState.Queued, restoredExecution.State);
        Assert.Null(restoredExecution.Lease);
        Assert.Null(restoredExecution.Request.SubmittedBySessionId);
        Assert.Equal("stable-client", restoredExecution.Request.SubmittedByClientId);
        Assert.Equal(5, restoredExecution.Request.MaxAttempts);
    }

    [Fact]
    public async Task ClaimedAndLeased_AreReleasedWithoutAttemptConsumption()
    {
        var source = Runtime();
        var session = Session(source, "worker");
        var task = source.Tasks.Create("Leased", steps: new[] { Step("aaia.test", new { }) });
        var execution = source.Scheduler.Enqueue(task.Id, maxAttempts: 3);
        Assert.True(source.Scheduler.TryAssignNext(out _));
        Assert.Equal(AiTaskStatus.Claimed, task.Status);
        Assert.Equal(1, source.Scheduler.Get(execution.Request.Id)!.AttemptCount);
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();

        var report = await Service(target).RestoreAsync(snapshot);

        Assert.Equal(1, report.ReleasedClaims);
        Assert.Equal(1, report.ReleasedLeases);
        Assert.Equal(AiTaskStatus.Pending, target.Tasks.Get(task.Id)!.Status);
        var restored = target.Scheduler.Get(execution.Request.Id)!;
        Assert.Equal(AiExecutionState.Queued, restored.State);
        Assert.Equal(0, restored.AttemptCount);
        Assert.Null(restored.Lease);
        Assert.Empty(target.Sessions.Active);
        _ = session;
    }

    [Fact]
    public async Task RunningTaskAndExecution_BecomeRecoveryRequiredWithoutExecution()
    {
        var source = Runtime();
        var session = Session(source, "worker");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Tasks.Executor = async (_, _, _, _) =>
        {
            await release.Task;
            return (true, "{}");
        };
        var task = source.Tasks.Create("Running", steps: new[] { Step("aaia.wait", new { value = 1 }) });
        var execution = source.Scheduler.Enqueue(task.Id);
        Assert.True(source.Scheduler.TryAssignNext(out _));
        var running = source.Scheduler.RunAsync(execution.Request.Id, session.SessionId);
        Assert.True(SpinWait.SpinUntil(
            () => source.Scheduler.Get(execution.Request.Id)?.State == AiExecutionState.Running,
            TimeSpan.FromSeconds(2)));
        var snapshot = await Service(source).CaptureAsync(1, Now);
        release.SetResult();
        await running;
        var target = Runtime();
        var executorCalls = 0;
        target.Tasks.Executor = (_, _, _, _) =>
        {
            Interlocked.Increment(ref executorCalls);
            return Task.FromResult((true, "{}"));
        };

        var report = await Service(target).RestoreAsync(snapshot);

        Assert.Equal(2, report.RecoveryRequiredCount);
        Assert.Equal(AiTaskStatus.RecoveryRequired, target.Tasks.Get(task.Id)!.Status);
        Assert.Equal(AiExecutionState.RecoveryRequired,
            target.Scheduler.Get(execution.Request.Id)!.State);
        Assert.False(target.Scheduler.TryAssignNext(out _));
        Assert.Equal(0, executorCalls);
        Assert.False(target.Scheduler.Cancel(execution.Request.Id));
    }

    [Fact]
    public async Task CancellingExecution_BecomesRecoveryRequired()
    {
        var source = Runtime();
        var session = Session(source, "worker");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Tasks.Executor = async (_, _, _, _) =>
        {
            await release.Task;
            return (true, "{}");
        };
        var task = source.Tasks.Create("Cancelling", steps: new[] { Step("aaia.wait", new { }) });
        var execution = source.Scheduler.Enqueue(task.Id);
        Assert.True(source.Scheduler.TryAssignNext(out _));
        var running = source.Scheduler.RunAsync(execution.Request.Id, session.SessionId);
        Assert.True(SpinWait.SpinUntil(
            () => source.Scheduler.Get(execution.Request.Id)?.State == AiExecutionState.Running,
            TimeSpan.FromSeconds(2)));
        Assert.True(source.Scheduler.Cancel(execution.Request.Id));
        Assert.Equal(AiExecutionState.Cancelling, source.Scheduler.Get(execution.Request.Id)!.State);
        var snapshot = await Service(source).CaptureAsync(1, Now);
        release.SetResult();
        await running;
        var target = Runtime();

        await Service(target).RestoreAsync(snapshot);

        Assert.Equal(AiExecutionState.RecoveryRequired,
            target.Scheduler.Get(execution.Request.Id)!.State);
    }

    [Fact]
    public async Task TerminalStates_RemainTerminal()
    {
        var source = Runtime();
        var session = Session(source, "worker");
        source.Tasks.Executor = (_, _, _, _) => Task.FromResult((true, "{}"));
        var task = source.Tasks.Create("Done", steps: new[] { Step("aaia.test", new { }) });
        var execution = source.Scheduler.Enqueue(task.Id);
        Assert.True(source.Scheduler.TryAssignNext(out _));
        await source.Scheduler.RunAsync(execution.Request.Id, session.SessionId);
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();

        await Service(target).RestoreAsync(snapshot);

        Assert.Equal(AiTaskStatus.Completed, target.Tasks.Get(task.Id)!.Status);
        Assert.Equal(AiExecutionState.Completed,
            target.Scheduler.Get(execution.Request.Id)!.State);
    }

    [Fact]
    public async Task RecoveryRetry_CreatesNewIdsAndDoesNotAutoRun()
    {
        var (runtime, originalTaskId, originalExecutionId) = await RestoredRecoveryRequiredRuntime();
        var service = Service(runtime);

        var retry = service.CreateRecoveryRetry(originalExecutionId, "admin");

        Assert.NotEqual(originalExecutionId, retry.Request.Id);
        Assert.NotEqual(originalTaskId, retry.Request.TaskId);
        Assert.Equal(AiExecutionState.Queued, retry.State);
        Assert.Equal(AiTaskStatus.Pending, runtime.Tasks.Get(retry.Request.TaskId)!.Status);
        Assert.All(runtime.Tasks.Get(retry.Request.TaskId)!.Steps,
            step => Assert.Equal(AiTaskStepStatus.Pending, step.Status));
        Assert.Equal(AiExecutionState.Failed, runtime.Scheduler.Get(originalExecutionId)!.State);
        Assert.Equal(AiTaskStatus.Failed, runtime.Tasks.Get(originalTaskId)!.Status);
        Assert.False(runtime.Scheduler.TryAssignNext(out _));
    }

    [Fact]
    public async Task RecoveryCanBeResolvedAsFailed()
    {
        var (runtime, taskId, executionId) = await RestoredRecoveryRequiredRuntime();
        var published = new List<AiRuntimeEvent>();
        runtime.Events.EventPublished += published.Add;

        var resolved = Service(runtime).ResolveRecoveryRequiredAsFailed(
            executionId, "admin", "manual_review_failed");

        Assert.True(resolved);
        Assert.Equal(AiTaskStatus.Failed, runtime.Tasks.Get(taskId)!.Status);
        Assert.Equal(AiExecutionState.Failed, runtime.Scheduler.Get(executionId)!.State);
        Assert.Contains(published, item => item.Type == AiRuntimeEventType.ExecutionRecoveryResolved);
    }

    [Fact]
    public async Task Scheduler_RemainsBlockedUntilAllRecoveryRequiredExecutionsAreResolved()
    {
        var (runtime, _, recoveryExecutionId) = await RestoredRecoveryRequiredRuntime();
        _ = Session(runtime, "fresh-worker");
        var queuedTask = runtime.Tasks.Create("Queued after recovery", steps: new[]
        {
            Step("aaia.test", new { value = 2 })
        });
        var queuedExecution = runtime.Scheduler.Enqueue(queuedTask.Id);

        Assert.False(runtime.Scheduler.TryAssignNext(out _));

        Assert.True(Service(runtime).ResolveRecoveryRequiredAsFailed(
            recoveryExecutionId, "admin", "manual_review_failed"));
        Assert.True(runtime.Scheduler.TryAssignNext(out var lease));
        Assert.Equal(queuedExecution.Request.Id, lease!.RequestId);
    }

    [Fact]
    public async Task RecoveryDecisionWithoutAuthorization_IsBlocked()
    {
        var (runtime, taskId, executionId) = await RestoredRecoveryRequiredRuntime();
        var service = new AiOrchestrationPersistenceService(
            runtime, new ContextBoundProtector(), "store", new FixedAuthorizer(false));

        var error = Assert.Throws<AiStateStoreException>(() =>
            service.CreateRecoveryRetry(executionId, "user"));

        Assert.Equal(AiRuntimeStateReasonCodes.RecoveryForbidden, error.ReasonCode);
        Assert.Equal(AiTaskStatus.RecoveryRequired, runtime.Tasks.Get(taskId)!.Status);
        Assert.Equal(AiExecutionState.RecoveryRequired,
            runtime.Scheduler.Get(executionId)!.State);
    }

    [Fact]
    public async Task RestoreIntoNonEmptyRuntime_IsRejectedWithoutMutation()
    {
        var source = Runtime();
        source.Tasks.Create("Persisted");
        var snapshot = await Service(source).CaptureAsync(1, Now);
        var target = Runtime();
        var existing = target.Tasks.Create("Existing");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Service(target).RestoreAsync(snapshot));

        Assert.Same(existing, target.Tasks.Get(existing.Id));
        Assert.Equal(1, target.Tasks.Count);
    }

    [Fact]
    public async Task WrongProtectorContext_FailsRestoreBeforeMutation()
    {
        var source = Runtime();
        source.Tasks.Create("Protected", steps: new[] { Step("aaia.test", new { value = 1 }) });
        var snapshot = await Service(source, storeId: "store-a").CaptureAsync(1, Now);
        var target = Runtime();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Service(target, storeId: "store-b").RestoreAsync(snapshot));

        Assert.Equal(0, target.Tasks.Count);
        Assert.Empty(target.Scheduler.List());
    }

    private static async Task<(AiRuntimeService Runtime, string TaskId, string ExecutionId)>
        RestoredRecoveryRequiredRuntime()
    {
        var source = Runtime();
        var session = Session(source, "worker");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Tasks.Executor = async (_, _, _, _) =>
        {
            await release.Task;
            return (true, "{}");
        };
        var task = source.Tasks.Create("Interrupted", steps: new[] { Step("aaia.wait", new { }) });
        var execution = source.Scheduler.Enqueue(task.Id, submittedByClientId: "stable-client");
        Assert.True(source.Scheduler.TryAssignNext(out _));
        var running = source.Scheduler.RunAsync(execution.Request.Id, session.SessionId);
        Assert.True(SpinWait.SpinUntil(
            () => source.Scheduler.Get(execution.Request.Id)?.State == AiExecutionState.Running,
            TimeSpan.FromSeconds(2)));
        var snapshot = await Service(source).CaptureAsync(1, Now);
        release.SetResult();
        await running;
        var target = Runtime();
        await Service(target).RestoreAsync(snapshot);
        return (target, task.Id, execution.Request.Id);
    }

    private static AiRuntimeService Runtime() => new(
        new AiToolRegistry(),
        new AiSessionManager(),
        new AiCapabilityManager(),
        new AiPermissionEngine(),
        new AiWorkspaceLockService(),
        new AiRuntimeEventBus(),
        new AiAuditService());

    private static AiSession Session(AiRuntimeService runtime, string name)
        => runtime.Sessions.Create(
            new AiClientIdentity { Name = name, Fingerprint = name },
            new[] { AiCapabilities.Mcp },
            AiPermission.Read);

    private static AiTaskStep Step(string tool, object input) => new()
    {
        ToolName = tool,
        Input = JsonSerializer.SerializeToElement(input)
    };

    private static AiOrchestrationPersistenceService Service(
        AiRuntimeService runtime,
        string storeId = "store")
        => new(runtime, new ContextBoundProtector(), storeId, new FixedAuthorizer(true));

    private sealed class FixedAuthorizer(bool allow) : IAiRecoveryAuthorizer
    {
        public bool IsAuthorized(string actorId, string action, out string? denialReason)
        {
            denialReason = allow ? null : "Owner/Admin required.";
            return allow;
        }
    }

    private sealed class ContextBoundProtector : IAiStateProtector
    {
        public string ProtectorId => "test-context-v1";

        public ValueTask<AiProtectedStatePayload> ProtectAsync(
            ReadOnlyMemory<byte> plaintext,
            AiStateProtectionContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var prefix = Encoding.UTF8.GetBytes(Key(context) + "\n");
            var result = new byte[prefix.Length + plaintext.Length];
            prefix.CopyTo(result, 0);
            plaintext.CopyTo(result.AsMemory(prefix.Length));
            return ValueTask.FromResult(new AiProtectedStatePayload
            {
                ProtectorId = ProtectorId,
                Ciphertext = result
            });
        }

        public ValueTask<byte[]> UnprotectAsync(
            AiProtectedStatePayload protectedPayload,
            AiStateProtectionContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (protectedPayload.ProtectorId != ProtectorId)
                throw new InvalidOperationException("Wrong protector.");
            var prefix = Encoding.UTF8.GetBytes(Key(context) + "\n");
            if (!protectedPayload.Ciphertext.AsSpan().StartsWith(prefix))
                throw new InvalidOperationException("Protection context mismatch.");
            return ValueTask.FromResult(protectedPayload.Ciphertext[prefix.Length..]);
        }

        private static string Key(AiStateProtectionContext context)
            => $"{context.StoreId}|{context.RecordType}|{context.RecordId}|{context.SchemaVersion}";
    }
}
