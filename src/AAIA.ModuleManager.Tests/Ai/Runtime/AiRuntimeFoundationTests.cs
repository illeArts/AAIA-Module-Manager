using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime;
using AAIA.ModuleManager.Services.Ai.Runtime.Hosts;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

/// <summary>
/// Tests der AI-Runtime-Foundation (Phase 7.0, Increment 1 + 1.1). Reines C#, kein MCP-SDK nötig.
/// </summary>
public sealed class AiRuntimeFoundationTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    private sealed class FakeStatusHost : IAiStatusHost
    {
        public string HostId => "test-host";
        public AaiaProjectStatus GetStatus() => new()
        {
            Version = "2.5.0-beta",
            ConnectorRunning = true,
            McpBridgeRunning = true,
            CurrentProject = @"C:\proj\fritzbox",
            PipelineStep = "Build",
            TrustLevel = "Unsigned",
            AaiasConnected = false
        };
    }

    private static AiRuntimeService BuildRuntime()
    {
        var runtime = new AiRuntimeService(
            new AiToolRegistry(), new AiSessionManager(), new AiCapabilityManager(),
            new AiPermissionEngine(), new AiWorkspaceLockService(), new AiRuntimeEventBus(),
            new AiAuditService());
        runtime.Hosts.Register<IAiStatusHost>(new FakeStatusHost());
        AaiaCoreToolsBootstrap.RegisterAll(runtime);
        return runtime;
    }

    private static AiSession FullSession(AiRuntimeService rt, AiPermission perms = (AiPermission)0xFFFF)
    {
        var identity = new AiClientIdentity { Name = "Claude Desktop", Version = "4.2", Vendor = "Anthropic", Fingerprint = "claude:4.2" };
        var caps = new[] { AiCapabilities.Mcp, AiCapabilities.ToolCalling, AiCapabilities.Events };
        return rt.Sessions.Create(identity, caps, perms);
    }

    [Fact] // 1 — Registry listet alle Kern-Tools
    public void Registry_Lists_All_Core_Tools()
    {
        var rt = BuildRuntime();
        var names = rt.Tools.ListActive().Select(t => t.Name).ToHashSet();
        foreach (var expected in new[]
        {
            "aaia.status.get", "aaia.project.create", "aaia.project.context.get",
            "aaia.files.allowed.list", "aaia.files.read", "aaia.patch.propose",
            "aaia.project.validate", "aaia.project.build", "aaia.project.package",
            "aaia.ide.open", "aaia.terminal.runSafe"
        })
            Assert.Contains(expected, names);
    }

    [Fact] // 2 — Black-Tools existieren nicht
    public void BlackTool_Registration_Throws()
    {
        var reg = new AiToolRegistry();
        Assert.Throws<System.InvalidOperationException>(() => reg.Register(new AiToolDefinition
        {
            Name = "aaia.evil", Description = "x", RiskLevel = AiRiskLevel.Black,
            InputSchema = Empty, Handler = (_, _) => Task.FromResult(AiToolResult.Ok(new { }))
        }));
    }

    [Fact] // 3 — Deaktiviertes Tool wird abgelehnt
    public async Task Disabled_Tool_Is_Rejected()
    {
        var rt = BuildRuntime();
        var s = FullSession(rt);
        rt.Tools.SetActive("aaia.status.get", false);
        var r = await rt.InvokeToolAsync(s.SessionId, "aaia.status.get", Empty);
        Assert.False(r.Success);
        Assert.Equal("tool_disabled", r.ErrorCode);
    }

    [Fact] // 4 — status.get liefert Host-Status
    public async Task StatusGet_Returns_Host_Status()
    {
        var rt = BuildRuntime();
        var s = FullSession(rt);
        var r = await rt.InvokeToolAsync(s.SessionId, "aaia.status.get", Empty);
        Assert.True(r.Success);
        Assert.Equal("AAIA Module Manager", r.Payload.GetProperty("app").GetString());
        Assert.Equal("2.5.0-beta", r.Payload.GetProperty("version").GetString());
        Assert.True(r.Payload.GetProperty("connectorRunning").GetBoolean());
    }

    [Fact] // 5 — Capability Negotiation filtert Tools
    public void Capability_Filtering_Hides_Events_Tool()
    {
        var rt = BuildRuntime();
        var noEvents = rt.Sessions.Create(
            new AiClientIdentity { Name = "Gemini", Fingerprint = "gemini" },
            new[] { AiCapabilities.Mcp }, AiPermission.Read);
        var offered = rt.Tools.ListForSession(noEvents).Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("aaia.events.subscribe", offered);
        Assert.Contains("aaia.status.get", offered);
    }

    [Fact] // 6 — Permission verweigert Build ohne Recht
    public async Task Build_Denied_Without_Permission()
    {
        var rt = BuildRuntime();
        var readOnly = rt.Sessions.Create(
            new AiClientIdentity { Name = "ChatGPT", Fingerprint = "chatgpt" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.Read);
        var input = JsonDocument.Parse("""{"projectPath":"C:\\proj\\x"}""").RootElement;
        var r = await rt.InvokeToolAsync(readOnly.SessionId, "aaia.project.build", input);
        Assert.False(r.Success);
        Assert.Equal("permission_denied", r.ErrorCode);
    }

    [Fact] // 7 — Workspace-Lock: Konflikt zwischen zwei Sessions, Release gibt frei
    public void WorkspaceLock_Conflict_Between_Sessions()
    {
        var rt = BuildRuntime();
        var a = FullSession(rt);
        var b = rt.Sessions.Create(new AiClientIdentity { Name = "ChatGPT", Fingerprint = "chatgpt" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.ProposePatch);

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "proj", "Program.cs");
        Assert.True(rt.Locks.TryAcquire(a, AiLockScope.File, path, out var lk, out _));
        Assert.False(rt.Locks.TryAcquire(b, AiLockScope.File, path, out _, out var conflict));
        Assert.NotNull(conflict);
        Assert.True(rt.Locks.Release(a, lk!.LockId));
        Assert.True(rt.Locks.TryAcquire(b, AiLockScope.File, path, out _, out _));
    }

    [Fact] // 8 — Lock-Timeout gibt automatisch frei
    public void WorkspaceLock_Timeout_Frees()
    {
        var rt = BuildRuntime();
        rt.Locks.DefaultTimeout = System.TimeSpan.FromMilliseconds(1);
        var a = FullSession(rt);
        var b = rt.Sessions.Create(new AiClientIdentity { Name = "ChatGPT", Fingerprint = "chatgpt" },
            AiCapabilityManager.DefaultMcpCapabilities(), AiPermission.ProposePatch);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "proj2");
        Assert.True(rt.Locks.TryAcquire(a, AiLockScope.Project, path, out _, out _));
        System.Threading.Thread.Sleep(20);
        Assert.True(rt.Locks.TryAcquire(b, AiLockScope.Project, path, out _, out _));
    }

    [Fact] // 9 — Sessions bleiben getrennt
    public void Sessions_Are_Isolated()
    {
        var rt = BuildRuntime();
        var a = rt.Sessions.Create(new AiClientIdentity { Name = "Claude", Fingerprint = "c" },
            null, AiPermission.Read | AiPermission.Build);
        var b = rt.Sessions.Create(new AiClientIdentity { Name = "ChatGPT", Fingerprint = "g" },
            null, AiPermission.Read);
        Assert.True(a.HasPermission(AiPermission.Build));
        Assert.False(b.HasPermission(AiPermission.Build));
        Assert.NotEqual(a.SessionId, b.SessionId);
        Assert.Equal(2, rt.Sessions.Count);
    }

    [Fact] // 10 — Tool-Versionierung: neue Version verdrängt deprecatete
    public void ToolVersioning_Resolves_Newest_NonDeprecated()
    {
        var reg = new AiToolRegistry();
        reg.Register(new AiToolDefinition { Name = "aaia.demo", Version = "1.0.0", Deprecated = true,
            Description = "old", RiskLevel = AiRiskLevel.Green, InputSchema = Empty,
            Handler = (_, _) => Task.FromResult(AiToolResult.Ok(new { v = 1 })) });
        reg.Register(new AiToolDefinition { Name = "aaia.demo", Version = "2.0.0",
            Description = "new", RiskLevel = AiRiskLevel.Green, InputSchema = Empty,
            Handler = (_, _) => Task.FromResult(AiToolResult.Ok(new { v = 2 })) });
        Assert.Equal("2.0.0", reg.Resolve("aaia.demo")!.Version);
    }

    [Fact] // 11 — Audit protokolliert mit Client-Identität
    public async Task Audit_Records_Client_Identity()
    {
        var rt = BuildRuntime();
        var s = FullSession(rt);
        await rt.InvokeToolAsync(s.SessionId, "aaia.status.get", Empty);
        var entry = rt.Audit.Recent(10).FirstOrDefault(e => e.Tool == "aaia.status.get");
        Assert.NotNull(entry);
        Assert.Contains("Claude", entry!.ClientIdentity);
        Assert.True(entry.Success);
    }

    [Fact] // 12 — Sign/Marketplace bleibt gesperrt, selbst wenn erteilt
    public async Task SignMarketplace_Always_Blocked()
    {
        var rt = BuildRuntime();
        rt.Tools.Register(new AiToolDefinition
        {
            Name = "aaia.sign.test", Description = "sign", RiskLevel = AiRiskLevel.Red,
            RequiredPermissions = AiPermission.Sign, InputSchema = Empty,
            Handler = (_, _) => Task.FromResult(AiToolResult.Ok(new { signed = true }))
        });
        var s = FullSession(rt, AiPermission.Read | AiPermission.Sign | AiPermission.Marketplace);
        var r = await rt.InvokeToolAsync(s.SessionId, "aaia.sign.test", Empty);
        Assert.False(r.Success);
        Assert.Equal("permission_denied", r.ErrorCode);
    }

    [Fact] // 13 — Unbekannte Session wird abgelehnt
    public async Task Unknown_Session_Rejected()
    {
        var rt = BuildRuntime();
        var r = await rt.InvokeToolAsync("does-not-exist", "aaia.status.get", Empty);
        Assert.False(r.Success);
        Assert.Equal("no_session", r.ErrorCode);
    }
}
