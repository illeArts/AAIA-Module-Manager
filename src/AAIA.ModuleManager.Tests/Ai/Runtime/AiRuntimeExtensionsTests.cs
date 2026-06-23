using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime;
using AAIA.ModuleManager.Services.Ai.Runtime.Collaboration;
using AAIA.ModuleManager.Services.Ai.Runtime.Hosts;
using AAIA.ModuleManager.Services.Ai.Runtime.Memory;
using AAIA.ModuleManager.Services.Ai.Runtime.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime.Workflows;
using Xunit;

namespace AAIA.ModuleManager.Tests.Ai.Runtime;

/// <summary>
/// Tests für Increment 1.1: Host-Abstraktion, AiTaskManager, AiSharedProjectState.
/// </summary>
public sealed class AiRuntimeExtensionsTests
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    private sealed class FakeStatusHost : IAiStatusHost
    {
        public string HostId => "test";
        public AaiaProjectStatus GetStatus() => new() { Version = "x", ConnectorRunning = true };
    }

    private sealed class FakeBuildHost : IAiBuildHost
    {
        public string HostId => "fake-build";
        public bool Called { get; private set; }
        public Task<AiHostResult> BuildAsync(string projectPath, bool restoreFirst, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(AiHostResult.Ok(new { success = true, summary = "build ok" }));
        }
    }

    private sealed class AllInOneHost : IAiValidationHost, IAiBuildHost, IAiPackageHost
    {
        public string HostId => "all-in-one";
        public Task<AiHostResult> ValidateAsync(string projectPath, CancellationToken ct)
            => Task.FromResult(AiHostResult.Ok(new { success = true }));
        public Task<AiHostResult> BuildAsync(string projectPath, bool restoreFirst, CancellationToken ct)
            => Task.FromResult(AiHostResult.Ok(new { success = true }));
        public Task<AiHostResult> PackageAsync(string projectPath, CancellationToken ct)
            => Task.FromResult(AiHostResult.Ok(new { success = true }));
    }

    private static AiRuntimeService BuildRuntime()
    {
        var rt = new AiRuntimeService(
            new AiToolRegistry(), new AiSessionManager(), new AiCapabilityManager(),
            new AiPermissionEngine(), new AiWorkspaceLockService(), new AiRuntimeEventBus(),
            new AiAuditService());
        rt.Hosts.Register<IAiStatusHost>(new FakeStatusHost());
        AaiaCoreToolsBootstrap.RegisterAll(rt);
        return rt;
    }

    private static AiSession Session(AiRuntimeService rt, string name, AiPermission perms)
        => rt.Sessions.Create(new AiClientIdentity { Name = name, Fingerprint = name }, null, perms);

    [Fact] // Host fehlt → host_unavailable, nichts verändert
    public async Task BuildTool_Without_Host_Returns_HostUnavailable()
    {
        var rt = BuildRuntime();
        var s = Session(rt, "Claude", AiPermission.Read | AiPermission.Build);
        var input = JsonDocument.Parse("""{"projectPath":"C:\\proj\\x"}""").RootElement;
        var r = await rt.InvokeToolAsync(s.SessionId, "aaia.project.build", input);
        Assert.False(r.Success);
        Assert.Equal("host_unavailable", r.ErrorCode);
    }

    [Fact] // Host registriert → Tool ruft den Host (Entkopplung über Interface)
    public async Task BuildTool_With_Host_Calls_Host()
    {
        var rt = BuildRuntime();
        var buildHost = new FakeBuildHost();
        rt.Hosts.Register<IAiBuildHost>(buildHost);
        var s = Session(rt, "Claude", AiPermission.Read | AiPermission.Build);
        var input = JsonDocument.Parse("""{"projectPath":"C:\\proj\\y"}""").RootElement;
        var r = await rt.InvokeToolAsync(s.SessionId, "aaia.project.build", input);
        Assert.True(r.Success);
        Assert.True(buildHost.Called);
        Assert.Equal("build ok", r.Payload.GetProperty("summary").GetString());
    }

    [Fact] // Task: erstellen → übernehmen → Schritte sequenziell ausführen
    public async Task Task_Create_Claim_Run_Completes()
    {
        var rt = BuildRuntime();
        var s = Session(rt, "Claude", AiPermission.Read);
        var task = rt.Tasks.Create("Status lesen", project: "proj", steps: new[]
        {
            new AiTaskStep { ToolName = "aaia.status.get", Input = Empty },
            new AiTaskStep { ToolName = "aaia.session.whoami", Input = Empty }
        });
        Assert.True(rt.Tasks.Claim(task.Id, s, out _));
        var done = await rt.Tasks.RunAsync(task.Id, s, CancellationToken.None);
        Assert.Equal(AiTaskStatus.Completed, done.Status);
        Assert.All(done.Steps, st => Assert.Equal(AiTaskStepStatus.Done, st.Status));
    }

    [Fact] // Task: zweite Session kann fremde Aufgabe nicht übernehmen
    public void Task_Claim_Conflict_Between_Sessions()
    {
        var rt = BuildRuntime();
        var a = Session(rt, "Claude", AiPermission.Read);
        var b = Session(rt, "ChatGPT", AiPermission.Read);
        var task = rt.Tasks.Create("X");
        Assert.True(rt.Tasks.Claim(task.Id, a, out _));
        Assert.False(rt.Tasks.Claim(task.Id, b, out var conflict));
        Assert.NotNull(conflict);
    }

    [Fact] // Blackboard: A bearbeitet, B sieht "nicht bearbeiten"
    public void Blackboard_InProgress_Blocks_Other_Session()
    {
        var rt = BuildRuntime();
        var a = Session(rt, "Claude", AiPermission.Read);
        var b = Session(rt, "ChatGPT", AiPermission.Read);

        Assert.True(rt.Blackboard.Write(a, "proj", "Login", AiWorkItemStatus.InProgress, "auth", out var item, out _));
        Assert.Equal("Claude", item.OwnerClientName);
        Assert.True(rt.Blackboard.IsOwnedByOther(b, "proj", "Login"));

        // B darf den Bereich nicht überschreiben, solange A InProgress ist.
        Assert.False(rt.Blackboard.Write(b, "proj", "Login", AiWorkItemStatus.InProgress, null, out _, out var conflict));
        Assert.NotNull(conflict);

        // A gibt frei → B kann übernehmen.
        Assert.True(rt.Blackboard.Write(a, "proj", "Login", AiWorkItemStatus.Done, null, out _, out _));
        Assert.False(rt.Blackboard.IsOwnedByOther(b, "proj", "Login"));
    }

    [Fact] // Workflow: Standard-Pipeline läuft Phasen sequenziell (mit Build-Host)
    public async Task Workflow_StandardPipeline_Runs_Phases()
    {
        var rt = BuildRuntime();
        // Hosts für validate/build/package registrieren (eine Klasse, mehrere Interfaces).
        var host = new AllInOneHost();
        rt.Hosts.Register<IAiValidationHost>(host);
        rt.Hosts.Register<IAiBuildHost>(host);
        rt.Hosts.Register<IAiPackageHost>(host);
        var s = Session(rt, "Claude", AiPermission.Read | AiPermission.Validate | AiPermission.Build | AiPermission.Package);

        var wf = rt.Workflows.Create("std", @"C:\proj", AiWorkflowEngine.StandardPipeline(@"C:\proj"));
        var done = await rt.Workflows.RunAsync(wf.Id, s, CancellationToken.None);
        Assert.Equal(AiWorkflowStatus.Completed, done.Status);
        Assert.All(done.Phases, p => Assert.Equal(AiWorkflowPhaseStatus.Done, p.Status));
    }

    [Fact] // Memory: Entscheidung festhalten und mit Autor abfragen
    public void Memory_Records_With_Author()
    {
        var rt = BuildRuntime();
        var s = Session(rt, "Claude", AiPermission.Read);
        rt.Memory.Record(s, "proj", "Login", "Argon2id statt bcrypt", AiMemoryKind.Decision, approvedBy: "André");
        var entries = rt.Memory.Query("proj", "Login");
        Assert.Single(entries);
        Assert.Equal("Claude", entries[0].AuthorClient);
        Assert.Equal("André", entries[0].ApprovedBy);
    }

    [Fact] // Collaboration: verteilt nach Rolle, schließt Autor beim Review aus
    public void Collaboration_Suggests_Reviewer_By_Role()
    {
        var rt = BuildRuntime();
        var dev = Session(rt, "Claude", AiPermission.Read);
        dev.Roles.Add(AAIA.ModuleManager.Services.Ai.Runtime.Roles.AiRole.Developer);
        var rev = Session(rt, "ChatGPT", AiPermission.Read);
        rev.Roles.Add(AAIA.ModuleManager.Services.Ai.Runtime.Roles.AiRole.Reviewer);

        var suggestion = rt.Collaboration.SuggestReviewer(dev.SessionId);
        Assert.NotNull(suggestion);
        Assert.Equal(rev.SessionId, suggestion!.SessionId);
    }

    [Fact] // Erweiterte Capability Negotiation aus Profil
    public void Capability_Profile_Maps_To_Tags()
    {
        var rt = BuildRuntime();
        var s = Session(rt, "Claude", AiPermission.Read);
        rt.Capabilities.Negotiate(s, new AiClientCapabilities
        {
            ContextWindowTokens = 200_000, Reasoning = true, Events = true, McpVersion = "2025-11-25"
        });
        Assert.True(s.HasCapability(AiCapabilities.LargeContext));
        Assert.True(s.HasCapability(AiCapabilities.Reasoning));
        Assert.True(s.HasCapability(AiCapabilities.Events));
        Assert.Equal("2025-11-25", s.CapabilityProfile!.McpVersion);
    }

    [Fact] // Host-Registry löst registrierte Hosts auf
    public void HostRegistry_Resolves_By_Interface()
    {
        var rt = BuildRuntime();
        Assert.True(rt.Hosts.Has<IAiStatusHost>());
        Assert.False(rt.Hosts.Has<IAiBuildHost>());
        rt.Hosts.Register<IAiBuildHost>(new FakeBuildHost());
        Assert.NotNull(rt.Hosts.Get<IAiBuildHost>());
    }
}
