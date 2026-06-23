using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.Air;
using AAIA.Air.Collaboration;
using AAIA.Air.Hosts;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

/// <summary>
/// Increment 2 — Nachweis: zwei Clients (Claude + Codex) gleichzeitig an einer Runtime,
/// getrennte Sessions/Permissions, koordiniert über das Blackboard.
/// </summary>
public sealed class MultiClientTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    private sealed class FakeStatusHost : IAiStatusHost
    {
        public string HostId => "test";
        public AaiaProjectStatus GetStatus() => new() { Version = "2.5.0", ConnectorRunning = true, McpBridgeRunning = true };
    }

    private sealed class FakeBuildHost : IAiBuildHost
    {
        public string HostId => "build";
        public Task<AiHostResult> BuildAsync(string p, bool r, CancellationToken ct)
            => Task.FromResult(AiHostResult.Ok(new { success = true }));
    }

    private static AiRuntimeService BuildRuntime()
    {
        var rt = new AiRuntimeService(
            new AiToolRegistry(), new AiSessionManager(), new AiCapabilityManager(),
            new AiPermissionEngine(), new AiWorkspaceLockService(), new AiRuntimeEventBus(),
            new AiAuditService());
        rt.Hosts.Register<IAiStatusHost>(new FakeStatusHost());
        rt.Hosts.Register<IAiBuildHost>(new FakeBuildHost());
        AaiaCoreToolsBootstrap.RegisterAll(rt);
        return rt;
    }

    [Fact]
    public async Task TwoClients_Concurrent_StatusGet_BothSucceed()
    {
        var rt = BuildRuntime();
        var claude = rt.Sessions.Create(new AiClientIdentity { Name = "Claude Desktop", Fingerprint = "claude" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.Read | AiPermission.Build);
        var codex = rt.Sessions.Create(new AiClientIdentity { Name = "Codex", Fingerprint = "codex" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.Read);

        var results = await Task.WhenAll(
            rt.InvokeToolAsync(claude.SessionId, "aaia.status.get", Empty),
            rt.InvokeToolAsync(codex.SessionId, "aaia.status.get", Empty));

        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(2, rt.Sessions.Count);
    }

    [Fact]
    public async Task PerClient_Permissions_Differ()
    {
        var rt = BuildRuntime();
        var claude = rt.Sessions.Create(new AiClientIdentity { Name = "Claude", Fingerprint = "claude" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.Read | AiPermission.Build);
        var codex = rt.Sessions.Create(new AiClientIdentity { Name = "Codex", Fingerprint = "codex" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.Read);

        var input = JsonDocument.Parse("""{"projectPath":"C:\\proj"}""").RootElement;
        var claudeBuild = await rt.InvokeToolAsync(claude.SessionId, "aaia.project.build", input);
        var codexBuild  = await rt.InvokeToolAsync(codex.SessionId, "aaia.project.build", input);

        Assert.True(claudeBuild.Success);                 // Claude darf bauen
        Assert.False(codexBuild.Success);                 // Codex nur Read
        Assert.Equal("permission_denied", codexBuild.ErrorCode);
    }

    [Fact]
    public void Blackboard_Coordinates_Two_Clients()
    {
        var rt = BuildRuntime();
        var claude = rt.Sessions.Create(new AiClientIdentity { Name = "Claude", Fingerprint = "claude" }, null, AiPermission.Read);
        var codex  = rt.Sessions.Create(new AiClientIdentity { Name = "Codex", Fingerprint = "codex" }, null, AiPermission.Read);

        Assert.True(rt.Blackboard.Write(claude, "proj", "Login", AiWorkItemStatus.InProgress, "auth", out _, out _));
        // Codex sieht: Login gehört Claude → nicht bearbeiten.
        Assert.True(rt.Blackboard.IsOwnedByOther(codex, "proj", "Login"));
        Assert.False(rt.Blackboard.Write(codex, "proj", "Login", AiWorkItemStatus.InProgress, null, out _, out var conflict));
        Assert.NotNull(conflict);
    }
}
