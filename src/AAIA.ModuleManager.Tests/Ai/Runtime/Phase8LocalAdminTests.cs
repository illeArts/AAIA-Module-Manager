using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Mcp;
using AAIA.ModuleManager.Services.Ai.Integration;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

public sealed class Phase8LocalAdminTests
{
    [Fact]
    public void NonAdministrator_CannotCancelExecution()
    {
        var panel = CreatePanel();
        var task = panel.Runtime.Tasks.Create("Protected");
        var execution = panel.Runtime.Scheduler.Enqueue(task.Id);

        var success = panel.TryCancelExecution(
            execution.Request.Id, "user", "requested", false, true, out var error);

        Assert.False(success);
        Assert.Contains("Administratorrechte", error);
        Assert.Equal(AiExecutionState.Queued, panel.Runtime.Scheduler.Get(execution.Request.Id)!.State);
        var audit = Assert.Single(panel.Runtime.Audit.Recent());
        Assert.False(audit.Success);
        Assert.Equal("air.admin.execution.cancel", audit.Tool);
    }

    [Fact]
    public void UnconfirmedAdministratorAction_IsRejected()
    {
        var panel = CreatePanel();
        var task = panel.Runtime.Tasks.Create("Protected");
        var execution = panel.Runtime.Scheduler.Enqueue(task.Id);

        var success = panel.TryCancelExecution(
            execution.Request.Id, "admin", "requested", true, false, out var error);

        Assert.False(success);
        Assert.Contains("nicht bestätigt", error);
        Assert.Equal(AiExecutionState.Queued, panel.Runtime.Scheduler.Get(execution.Request.Id)!.State);
    }

    [Fact]
    public void ConfirmedAdministrator_CanCancelAndIsAudited()
    {
        var panel = CreatePanel();
        var task = panel.Runtime.Tasks.Create("Cancel me");
        var execution = panel.Runtime.Scheduler.Enqueue(task.Id);

        var success = panel.TryCancelExecution(
            execution.Request.Id, "admin", "duplicate work", true, true, out var error);

        Assert.True(success, error);
        Assert.Equal(AiExecutionState.Cancelled, panel.Runtime.Scheduler.Get(execution.Request.Id)!.State);
        var audit = Assert.Single(panel.Runtime.Audit.Recent());
        Assert.Equal("local-admin", audit.SessionId);
        Assert.Equal("air.admin.execution.cancel", audit.Tool);
        Assert.Contains("duplicate work", audit.Detail);
    }

    [Fact]
    public void ConfirmedAdministrator_CanDisableResourceAndIsAudited()
    {
        var panel = CreatePanel();
        panel.Runtime.Resources.Registry.Register(Profile());

        var success = panel.TrySetResourceEnabled(
            "resource-1", false, "admin", "maintenance", true, true, out var error);

        Assert.True(success, error);
        Assert.False(panel.Runtime.Resources.Registry.GetProfile("resource-1")!.Enabled);
        panel.Runtime.Resources.Registry.RegisterOrUpdate(Profile());
        Assert.False(panel.Runtime.Resources.Registry.GetProfile("resource-1")!.Enabled);
        Assert.Contains(panel.Runtime.Audit.Recent(), entry =>
            entry.Tool == "air.admin.resource.enabled" && entry.Success);
    }

    [Fact]
    public void ConfirmedAdministrator_CanCreateBudgetWithoutMcpTool()
    {
        var panel = CreatePanel();
        var now = DateTime.UtcNow;
        var budget = new AiResourceBudget
        {
            Scope = AiBudgetScope.Runtime,
            CostUnit = "EUR",
            Window = AiBudgetWindow.Day,
            HardLimit = 25,
            WindowStartsAtUtc = now,
            WindowEndsAtUtc = now.AddDays(1)
        };

        var success = panel.TryCreateBudget(
            budget, "admin", "daily guardrail", true, true, out var error);

        Assert.True(success, error);
        Assert.Single(panel.Runtime.Resources.ListBudgets());
        Assert.Null(panel.Runtime.Tools.Resolve("aaia.resource.budget.set"));
        Assert.Contains(panel.Runtime.Audit.Recent(), entry =>
            entry.Tool == "air.admin.resource.budget" && entry.Success);
    }

    private static AiRuntimeConnectorPanel CreatePanel()
        => new(new AaiaMcpBridgeOptions(), new Bridge());

    private static AiResourceProfile Profile() => new()
    {
        ResourceId = "resource-1",
        ProviderId = "test-host",
        Kind = AiResourceKind.Compute,
        Capacity = new AiResourceCapacity { MaxConcurrentExecutions = 1 }
    };

    private sealed class Bridge : IModuleManagerAiBridge
    {
        public AaiaProjectStatus GetStatus() => new();
        public ModuleManagerProjectInfo? ResolveProject(string projectPath) => null;
        public Task<AiHostResult> ApproveAndApplyPatchAsync(AiPatchProposalInput input, CancellationToken ct)
            => Task.FromResult(AiHostResult.Fail("not available"));
        public Task<AiHostResult> CreateProjectAsync(AiProjectCreateInput input, CancellationToken ct)
            => Task.FromResult(AiHostResult.Fail("not available"));
    }
}
