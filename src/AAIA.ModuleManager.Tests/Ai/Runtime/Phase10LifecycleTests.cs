using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Persistence;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase10LifecycleTests
{
    [Fact]
    public async Task Lifecycle_StartsAdapterOnlyAfterPersistenceReady()
    {
        var runtime = Runtime();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend);
        var calls = new List<string>();
        await using var lifecycle = new AiRuntimeLifecycle(
            runtime,
            coordinator,
            adapterStart: _ =>
            {
                calls.Add($"adapter-start:{runtime.PersistenceStatus}");
                return ValueTask.CompletedTask;
            },
            adapterStop: _ =>
            {
                calls.Add("adapter-stop");
                return ValueTask.CompletedTask;
            });

        var status = await lifecycle.InitializeAsync();

        Assert.Equal(AiRuntimeRecoveryStatus.Ready, status);
        Assert.Equal(new[] { "adapter-start:Ready" }, calls);
    }

    [Fact]
    public async Task ReadinessLease_IsInvalidAfterStop()
    {
        var runtime = Runtime();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend);
        await using var lifecycle = new AiRuntimeLifecycle(runtime, coordinator);
        await lifecycle.InitializeAsync();
        using var lease = lifecycle.CreateReadinessLease();

        await lifecycle.StopAsync();

        Assert.False(lease.IsValid);
        var error = Assert.Throws<AiStateStoreException>(() => lease.ThrowIfExpired());
        Assert.Equal(AiRuntimeStateReasonCodes.ReadinessExpired, error.ReasonCode);
    }

    [Fact]
    public async Task DurableToolInvocation_IsRejectedAfterLifecycleStop()
    {
        var runtime = RuntimeWithDurableTaskTool();
        var backend = new AiInMemoryRuntimeStateStoreBackend("store");
        await using var coordinator = Coordinator(runtime, backend);
        await using var lifecycle = new AiRuntimeLifecycle(runtime, coordinator);
        await lifecycle.InitializeAsync();
        await lifecycle.StopAsync();
        var session = runtime.Sessions.Create(
            new AiClientIdentity { Name = "client", Fingerprint = "client" },
            grantedPermissions: AiPermission.Read);

        var result = await runtime.InvokeToolAsync(
            session.SessionId,
            "aaia.task.create",
            JsonSerializer.SerializeToElement(new { title = "blocked" }));

        Assert.False(result.Success);
        Assert.Equal(AiRuntimeStateReasonCodes.ReadinessExpired, result.ErrorCode);
        Assert.Empty(runtime.Tasks.List());
    }

    private static AiRuntimePersistenceCoordinator Coordinator(
        AiRuntimeService runtime,
        AiInMemoryRuntimeStateStoreBackend backend)
        => new(runtime, new AiInMemoryRuntimeStateStore(backend), new PassthroughProtector(),
            new AiRuntimePersistenceOptions { Enabled = true, UseTypedDeltaWriter = true },
            "runtime");

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
}
