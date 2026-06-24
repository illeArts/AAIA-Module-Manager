using System.Text.Json;
using AAIA.Air;
using AAIA.Air.Contracts;
using AAIA.Air.Mcp;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Mcp;

public sealed class Phase8AdapterTests
{
    [Fact]
    public void NewPhase8Options_DefaultToClosed()
    {
        var options = new AaiaMcpBridgeOptions();

        Assert.False(options.AllowCollaboration);
        Assert.False(options.AllowScheduling);
        Assert.False(options.AllowResourceRead);
    }

    [Fact]
    public void DefaultPermissions_DoNotContainPhase8Mutations()
    {
        var composition = Create(new AaiaMcpBridgeOptions());

        var permissions = composition.Adapter.DefaultPermissionsFromOptions();

        Assert.False(permissions.HasFlag(AiPermission.Collaborate));
        Assert.False(permissions.HasFlag(AiPermission.Schedule));
        Assert.False(permissions.HasFlag(AiPermission.ManageResources));
    }

    [Fact]
    public void ExplicitOptions_GrantOnlyCollaborationAndScheduling()
    {
        var composition = Create(new AaiaMcpBridgeOptions
        {
            AllowCollaboration = true,
            AllowScheduling = true,
            AllowResourceRead = true
        });

        var permissions = composition.Adapter.DefaultPermissionsFromOptions();

        Assert.True(permissions.HasFlag(AiPermission.Collaborate));
        Assert.True(permissions.HasFlag(AiPermission.Schedule));
        Assert.False(permissions.HasFlag(AiPermission.ManageResources));
    }

    [Fact]
    public void ExplicitToggleChange_UpdatesOnlyPhase8RightsOfActiveSessions()
    {
        var options = new AaiaMcpBridgeOptions { AllowBuild = true };
        var composition = Create(options);
        var session = Session(composition, "active");
        options.AllowCollaboration = true;
        options.AllowScheduling = true;

        composition.Adapter.ApplyPhase8OptionsToActiveSessions();

        Assert.True(session.GrantedPermissions.HasFlag(AiPermission.Build));
        Assert.True(session.GrantedPermissions.HasFlag(AiPermission.Collaborate));
        Assert.True(session.GrantedPermissions.HasFlag(AiPermission.Schedule));
        Assert.False(session.GrantedPermissions.HasFlag(AiPermission.ManageResources));
    }

    [Fact]
    public void ToolListing_RequiresExactMutationPermission()
    {
        var closed = Create(new AaiaMcpBridgeOptions());
        var closedSession = Session(closed, "closed");
        var closedTools = closed.Adapter.ListTools(closedSession.SessionId).Select(t => t.Name).ToHashSet();

        var open = Create(new AaiaMcpBridgeOptions { AllowCollaboration = true, AllowScheduling = true });
        var openSession = Session(open, "open");
        var openTools = open.Adapter.ListTools(openSession.SessionId).Select(t => t.Name).ToHashSet();

        Assert.DoesNotContain("aaia.message.send", closedTools);
        Assert.DoesNotContain("aaia.execution.enqueue", closedTools);
        Assert.Contains("aaia.message.inbox", closedTools);
        Assert.Contains("aaia.execution.list", closedTools);
        Assert.Contains("aaia.message.send", openTools);
        Assert.Contains("aaia.execution.enqueue", openTools);
    }

    [Fact]
    public async Task HiddenMutationTool_CannotBeCalledDirectly()
    {
        var composition = Create(new AaiaMcpBridgeOptions());
        var session = Session(composition, "closed");

        var result = await Call(composition, session, "aaia.message.send", new
        {
            receiver = "broadcast",
            idempotencyId = "hidden-1"
        });

        Assert.False(result.Success);
        Assert.Equal("unknown_tool", result.Json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MessageSender_IsBoundToAuthenticatedSession()
    {
        var composition = Create(new AaiaMcpBridgeOptions { AllowCollaboration = true });
        var sender = Session(composition, "sender");
        var receiver = Session(composition, "receiver");

        var sent = await Call(composition, sender, "aaia.message.send", new
        {
            receiver = receiver.SessionId,
            sender = "spoofed",
            subject = "Review",
            payload = "Bitte prüfen",
            idempotencyId = "message-1"
        });
        var inbox = await Call(composition, receiver, "aaia.message.inbox", new { });

        Assert.True(sent.Success);
        var message = Assert.Single(inbox.Json.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal(sender.SessionId, message.GetProperty("sender").GetString());
    }

    [Fact]
    public async Task DuplicateMessageIdempotencyId_CreatesOneDelivery()
    {
        var composition = Create(new AaiaMcpBridgeOptions { AllowCollaboration = true });
        var sender = Session(composition, "sender");
        var receiver = Session(composition, "receiver");
        var input = new { receiver = receiver.SessionId, payload = "once", idempotencyId = "same-message" };

        var first = await Call(composition, sender, "aaia.message.send", input);
        var second = await Call(composition, sender, "aaia.message.send", input);
        var inbox = await Call(composition, receiver, "aaia.message.inbox", new { });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Single(inbox.Json.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal(
            first.Json.RootElement.GetProperty("messageId").GetString(),
            second.Json.RootElement.GetProperty("messageId").GetString());
    }

    [Fact]
    public async Task ReusedIdempotencyId_WithDifferentPayload_IsRejected()
    {
        var composition = Create(new AaiaMcpBridgeOptions { AllowCollaboration = true });
        var sender = Session(composition, "sender");
        var receiver = Session(composition, "receiver");

        Assert.True((await Call(composition, sender, "aaia.message.send", new
        {
            receiver = receiver.SessionId, payload = "first", idempotencyId = "conflict"
        })).Success);
        var conflict = await Call(composition, sender, "aaia.message.send", new
        {
            receiver = receiver.SessionId, payload = "second", idempotencyId = "conflict"
        });

        Assert.False(conflict.Success);
        Assert.Equal(AiPhase8ErrorCodes.IdempotencyConflict,
            conflict.Json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExecutionViewsAndCancellation_AreOwnerIsolated()
    {
        var composition = Create(new AaiaMcpBridgeOptions { AllowScheduling = true });
        var owner = Session(composition, "owner");
        var stranger = Session(composition, "stranger");
        var task = composition.Runtime.Tasks.Create("Owned task");

        var queued = await Call(composition, owner, "aaia.execution.enqueue", new
        {
            taskId = task.Id,
            idempotencyId = "execution-1"
        });
        var executionId = queued.Json.RootElement.GetProperty("executionId").GetString()!;
        var strangerGet = await Call(composition, stranger, "aaia.execution.get", new { executionId });
        var strangerCancel = await Call(composition, stranger, "aaia.execution.cancel", new { executionId });
        var ownerCancel = await Call(composition, owner, "aaia.execution.cancel", new { executionId });

        Assert.True(queued.Success);
        Assert.False(strangerGet.Success);
        Assert.Equal(AiPhase8ErrorCodes.NotOwner, strangerGet.Json.RootElement.GetProperty("code").GetString());
        Assert.False(strangerCancel.Success);
        Assert.True(ownerCancel.Success);
        Assert.Equal(AiExecutionState.Cancelled, composition.Runtime.Scheduler.Get(executionId)!.State);
    }

    [Fact]
    public async Task DuplicateExecutionIdempotencyId_CreatesOneQueueEntry()
    {
        var composition = Create(new AaiaMcpBridgeOptions { AllowScheduling = true });
        var session = Session(composition, "scheduler");
        var task = composition.Runtime.Tasks.Create("Once");
        var input = new { taskId = task.Id, idempotencyId = "same-execution" };

        var first = await Call(composition, session, "aaia.execution.enqueue", input);
        var second = await Call(composition, session, "aaia.execution.enqueue", input);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Single(composition.Runtime.Scheduler.List());
        Assert.Equal(
            first.Json.RootElement.GetProperty("executionId").GetString(),
            second.Json.RootElement.GetProperty("executionId").GetString());
    }

    [Fact]
    public async Task ResourceTools_AreGatedAndRedacted()
    {
        var closed = Create(new AaiaMcpBridgeOptions());
        var closedSession = Session(closed, "closed");
        Assert.DoesNotContain("aaia.resource.list", closed.Adapter.ListTools(closedSession.SessionId).Select(t => t.Name));

        var open = Create(new AaiaMcpBridgeOptions { AllowResourceRead = true });
        var openSession = Session(open, "open");
        open.Runtime.Resources.Registry.Register(new AiResourceProfile
        {
            ResourceId = "resource-1",
            ProviderId = "secret-provider",
            DisplayName = "Vendor Model",
            Kind = AiResourceKind.Inference,
            Capacity = new AiResourceCapacity { MaxConcurrentExecutions = 2 },
            CostRate = new AiResourceCostRate { CostUnit = "EUR", FixedPerExecution = 99 }
        });

        var result = await Call(open, openSession, "aaia.resource.list", new { });
        var json = result.Json.RootElement.GetRawText();

        Assert.True(result.Success);
        Assert.Contains("resource-1", json);
        Assert.DoesNotContain("secret-provider", json);
        Assert.DoesNotContain("Vendor Model", json);
        Assert.DoesNotContain("EUR", json);
        Assert.DoesNotContain("99", json);
    }

    [Fact]
    public void ResourceMutationTools_AreNeverRegistered()
    {
        var composition = Create(new AaiaMcpBridgeOptions
        {
            AllowCollaboration = true,
            AllowScheduling = true,
            AllowResourceRead = true,
            AllowSignMarketplace = true
        });

        var tools = composition.Runtime.Tools.ListActive().Select(t => t.Name).ToArray();

        Assert.DoesNotContain(tools, name => name is "aaia.resource.register" or "aaia.resource.telemetry.update"
            or "aaia.resource.budget.set" or "aaia.resource.reserve" or "aaia.resource.commit" or "aaia.resource.release");
    }

    [Fact]
    public void ResourceHost_ImportsNormalizedProfileAndTelemetryOnly()
    {
        var host = new ResourceHost("host-1");
        var composition = new AiRuntimeComposition(new AaiaMcpBridgeOptions(), host);
        var permissions = composition.Runtime.Permissions.DefaultPermissions;

        var changed = composition.RefreshResourceHost();

        Assert.Equal(2, changed);
        Assert.Equal("host-1", composition.Runtime.Resources.Registry.GetProfile("resource-1")!.ProviderId);
        Assert.True(composition.Runtime.Resources.Registry.GetTelemetry("resource-1")!.Healthy);
        Assert.Equal(permissions, composition.Runtime.Permissions.DefaultPermissions);
        Assert.Empty(composition.Runtime.Scheduler.List());
    }

    [Fact]
    public void ResourceHost_CannotSpoofProviderIdentity()
    {
        var composition = new AiRuntimeComposition(new AaiaMcpBridgeOptions(), new ResourceHost("other-host"));

        Assert.Throws<InvalidOperationException>(() => composition.RefreshResourceHost());
    }

    private static AiRuntimeComposition Create(AaiaMcpBridgeOptions options) => new(options);

    private static AiSession Session(AiRuntimeComposition composition, string name)
        => composition.Adapter.ResolveSession(name, new AiClientIdentity { Name = name, Fingerprint = name },
            AiCapabilityManager.DefaultMcpCapabilities());

    private static async Task<CallResult> Call(
        AiRuntimeComposition composition,
        AiSession session,
        string tool,
        object input)
    {
        var element = JsonSerializer.SerializeToElement(input);
        var (success, json) = await composition.Adapter.CallToolAsync(session.SessionId, tool, element, CancellationToken.None);
        return new CallResult(success, JsonDocument.Parse(json));
    }

    private sealed record CallResult(bool Success, JsonDocument Json);

    private sealed class ResourceHost(string hostId) : IAiResourceHost
    {
        public string HostId => hostId;

        public IReadOnlyList<AiResourceProfile> GetResourceProfiles() => new[]
        {
            new AiResourceProfile
            {
                ResourceId = "resource-1",
                ProviderId = "host-1",
                Kind = AiResourceKind.Compute,
                Capacity = new AiResourceCapacity { MaxConcurrentExecutions = 1 }
            }
        };

        public IReadOnlyList<AiResourceTelemetry> GetResourceTelemetry() => new[]
        {
            new AiResourceTelemetry
            {
                ResourceId = "resource-1",
                ObservedAtUtc = DateTime.UtcNow,
                Healthy = true
            }
        };
    }
}
