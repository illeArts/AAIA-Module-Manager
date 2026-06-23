using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime.Collaboration;
using AAIA.ModuleManager.Services.Ai.Runtime.Hosts;
using AAIA.ModuleManager.Services.Ai.Runtime.Memory;
using AAIA.ModuleManager.Services.Ai.Runtime.Tasks;
using AAIA.ModuleManager.Services.Ai.Runtime.Workflows;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>
/// Registriert Runtime-Tools, Kern-Tools (gegen Host-Interfaces), Task- und
/// Collaboration-Tools. Die Tools resolven ihre Hosts zur Laufzeit aus der
/// <see cref="AiHostRegistry"/> — der Kern kennt KEINE konkrete App.
///
/// Ist der zuständige Host (noch) nicht registriert, liefert das Tool ein
/// strukturiertes "host_unavailable" und verändert nichts. In Increment 2 registriert
/// der Module Manager die echten Hosts; andere Apps (AAIAS, BBK, DUKI) später ebenso.
/// </summary>
public static class AaiaCoreToolsBootstrap
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonElement EmptyObjectSchema =
        JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement.Clone();

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static void RegisterAll(AiRuntimeService runtime)
    {
        var reg = runtime.Tools;

        RegisterRuntimeTools(runtime, reg);
        RegisterHostBackedCoreTools(runtime, reg);
        RegisterTaskTools(runtime, reg);
        RegisterWorkflowTools(runtime, reg);
        RegisterBlackboardTools(runtime, reg);
        RegisterMemoryTools(runtime, reg);
    }

    // ── Runtime-Tools (voll funktionsfähig, host-frei) ─────────────────────────

    private static void RegisterRuntimeTools(AiRuntimeService runtime, AiToolRegistry reg)
    {
        reg.Register(new AiToolDefinition
        {
            Name = "aaia.status.get", Version = "1.0.0", Since = "7.0.0",
            Description = "Liest den sicheren Status (ohne Secrets) über den registrierten Status-Host.",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = EmptyObjectSchema,
            Handler = (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiStatusHost>();
                if (host is null) return Task.FromResult(AiToolResult.Fail("Kein Status-Host registriert.", "host_unavailable"));
                var s = host.GetStatus();
                return Task.FromResult(AiToolResult.Ok(new
                {
                    app = s.App, version = s.Version, connectorRunning = s.ConnectorRunning,
                    mcpBridgeRunning = s.McpBridgeRunning, currentProject = s.CurrentProject,
                    pipelineStep = s.PipelineStep, trustLevel = s.TrustLevel, aaiasConnected = s.AaiasConnected
                }));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.session.whoami", Version = "1.0.0", Since = "7.0.0",
            Description = "Gibt die eigene Session, Capabilities und Permissions zurück.",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = EmptyObjectSchema,
            Handler = (inv, ct) =>
            {
                var ses = inv.Session;
                return Task.FromResult(AiToolResult.Ok(new
                {
                    sessionId = ses.SessionId, client = ses.Identity.ToString(), vendor = ses.Vendor,
                    model = ses.Model, connectedAt = ses.ConnectedAt, currentProject = ses.CurrentProject,
                    capabilities = ses.Capabilities.ToArray(), permissions = ses.GrantedPermissions.ToString(),
                    activeLocks = ses.ActiveLocks.ToArray()
                }));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.events.subscribe", Version = "1.0.0", Since = "7.0.0",
            Description = "Abonniert Pipeline-/Runtime-Ereignisse (subscribe.pipeline).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            RequiredCapabilities = new[] { AiCapabilities.Events },
            InputSchema = EmptyObjectSchema,
            Handler = (inv, ct) => Task.FromResult(AiToolResult.Ok(new
            {
                subscribed = true,
                note = "Ereignisse werden über den MCP-Transport gepusht. Ohne Events-Capability bleibt Polling via aaia.status.get."
            }))
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.locks.acquire", Version = "1.0.0", Since = "7.0.0",
            Description = "Fordert einen Workspace-Lock an (Project/Folder/File).",
            RiskLevel = AiRiskLevel.Yellow, RequiredPermissions = AiPermission.ProposePatch,
            InputSchema = Schema("""
                {"type":"object","properties":{
                    "projectPath":{"type":"string"},
                    "scope":{"type":"string","enum":["Project","Folder","File"]},
                    "path":{"type":"string"}},"required":["path"]}
                """),
            Handler = (inv, ct) =>
            {
                var path = ReadString(inv.Input, "path") ?? ReadString(inv.Input, "projectPath");
                if (string.IsNullOrEmpty(path)) return Task.FromResult(AiToolResult.Fail("Pfad fehlt.", "bad_input"));
                var scope = Enum.TryParse<AiLockScope>(ReadString(inv.Input, "scope"), true, out var sc) ? sc : AiLockScope.File;
                if (runtime.Locks.TryAcquire(inv.Session, scope, path!, out var lk, out var conflict))
                    return Task.FromResult(AiToolResult.Ok(new { lockId = lk!.LockId, scope = lk.Scope.ToString(), path = lk.NormalizedPath }));
                return Task.FromResult(AiToolResult.Fail(conflict ?? "Lock-Konflikt.", "locked"));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.locks.release", Version = "1.0.0", Since = "7.0.0",
            Description = "Gibt einen zuvor erworbenen Workspace-Lock frei.",
            RiskLevel = AiRiskLevel.Yellow, RequiredPermissions = AiPermission.ProposePatch,
            InputSchema = Schema("""{"type":"object","properties":{"lockId":{"type":"string"}},"required":["lockId"]}"""),
            Handler = (inv, ct) =>
            {
                var lockId = ReadString(inv.Input, "lockId") ?? "";
                var ok = runtime.Locks.Release(inv.Session, lockId);
                return Task.FromResult(ok
                    ? AiToolResult.Ok(new { released = true, lockId })
                    : AiToolResult.Fail("Lock nicht gefunden oder fremde Session.", "not_found"));
            }
        });
    }

    // ── Kern-Tools gegen Host-Interfaces ───────────────────────────────────────

    private static void RegisterHostBackedCoreTools(AiRuntimeService runtime, AiToolRegistry reg)
    {
        var projectSchema = Schema("""{"type":"object","properties":{"projectPath":{"type":"string"}},"required":["projectPath"]}""");

        reg.Register(Core("aaia.project.create", AiRiskLevel.Yellow, AiPermission.CreateProject, true,
            "Erstellt ein neues Modulprojekt über den Projekt-Host (Wizard/Scaffold).",
            Schema("""{"type":"object","properties":{"idea":{"type":"string"},"extensionKind":{"type":"string"},"host":{"type":"string"},"riskPreference":{"type":"string"}},"required":["idea"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiProjectHost>();
                if (host is null) return HostMissing("IAiProjectHost");
                var input = Bind<AiProjectCreateInput>(inv.Input) ?? new AiProjectCreateInput();
                return Wrap(host.CreateProjectAsync(input, ct));
            }));

        reg.Register(Core("aaia.project.context.get", AiRiskLevel.Green, AiPermission.Read, false,
            "Liefert sicheren Projektkontext (ohne Secrets) über den Projekt-Host.",
            projectSchema,
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiProjectHost>();
                if (host is null) return HostMissing("IAiProjectHost");
                return Wrap(host.GetContextAsync(ReadString(inv.Input, "projectPath") ?? "", ct));
            }));

        reg.Register(Core("aaia.files.allowed.list", AiRiskLevel.Green, AiPermission.Read, false,
            "Listet erlaubte Projektdateien (ohne Secrets/Private Keys) über den Datei-Host.",
            Schema("""{"type":"object","properties":{"projectPath":{"type":"string"},"includeContent":{"type":"boolean"}},"required":["projectPath"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiFileHost>();
                if (host is null) return HostMissing("IAiFileHost");
                var inc = inv.Input.TryGetProperty("includeContent", out var v) && v.ValueKind == JsonValueKind.True;
                return Wrap(host.ListAllowedAsync(ReadString(inv.Input, "projectPath") ?? "", inc, ct));
            }));

        reg.Register(Core("aaia.files.read", AiRiskLevel.Green, AiPermission.Read, false,
            "Liest eine erlaubte Projektdatei (Limit/Path-Traversal-Schutz im Host).",
            Schema("""{"type":"object","properties":{"projectPath":{"type":"string"},"relativePath":{"type":"string"}},"required":["projectPath","relativePath"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiFileHost>();
                if (host is null) return HostMissing("IAiFileHost");
                return Wrap(host.ReadAsync(ReadString(inv.Input, "projectPath") ?? "", ReadString(inv.Input, "relativePath") ?? "", ct));
            }));

        reg.Register(Core("aaia.patch.propose", AiRiskLevel.Yellow, AiPermission.ProposePatch, true,
            "Reicht einen Patch über den Patch-Host (Approval-Workflow) ein.",
            Schema("""{"type":"object","properties":{"projectPath":{"type":"string"},"reason":{"type":"string"},"changes":{"type":"array"}},"required":["projectPath","changes"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiPatchHost>();
                if (host is null) return HostMissing("IAiPatchHost");
                var input = Bind<AiPatchProposalInput>(inv.Input) ?? new AiPatchProposalInput();
                return Wrap(host.ProposeAsync(input, ct));
            }));

        reg.Register(Core("aaia.project.validate", AiRiskLevel.Orange, AiPermission.Validate, false,
            "Startet die Projektvalidierung über den Validierungs-Host.",
            projectSchema,
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiValidationHost>();
                if (host is null) return HostMissing("IAiValidationHost");
                return Wrap(host.ValidateAsync(ReadString(inv.Input, "projectPath") ?? "", ct));
            }));

        reg.Register(Core("aaia.project.build", AiRiskLevel.Orange, AiPermission.Build, false,
            "Baut das Projekt über den Build-Host; Buildfehler strukturiert.",
            Schema("""{"type":"object","properties":{"projectPath":{"type":"string"},"restoreFirst":{"type":"boolean"}},"required":["projectPath"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiBuildHost>();
                if (host is null) return HostMissing("IAiBuildHost");
                var restore = !inv.Input.TryGetProperty("restoreFirst", out var v) || v.ValueKind != JsonValueKind.False;
                return Wrap(host.BuildAsync(ReadString(inv.Input, "projectPath") ?? "", restore, ct));
            }));

        reg.Register(Core("aaia.project.package", AiRiskLevel.Orange, AiPermission.Package, true,
            "Paketiert über den Package-Host (keine eigene ZIP-Logik).",
            projectSchema,
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiPackageHost>();
                if (host is null) return HostMissing("IAiPackageHost");
                return Wrap(host.PackageAsync(ReadString(inv.Input, "projectPath") ?? "", ct));
            }));

        reg.Register(Core("aaia.ide.open", AiRiskLevel.Orange, AiPermission.OpenIde, true,
            "Öffnet das Projekt in der gewählten IDE über den IDE-Host (nur Projektpfad).",
            Schema("""{"type":"object","properties":{"projectPath":{"type":"string"},"ide":{"type":"string","enum":["visualstudio","vscode","xcode","rider","default"]}},"required":["projectPath"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiIdeHost>();
                if (host is null) return HostMissing("IAiIdeHost");
                return Wrap(host.OpenAsync(ReadString(inv.Input, "projectPath") ?? "", ReadString(inv.Input, "ide") ?? "default", ct));
            }));

        reg.Register(Core("aaia.terminal.runSafe", AiRiskLevel.Orange, AiPermission.RunSafeTerminal, true,
            "Führt nur Allowlist-Kommandos im Projektordner aus (über den Terminal-Host).",
            Schema("""{"type":"object","properties":{"projectPath":{"type":"string"},"command":{"type":"string"}},"required":["projectPath","command"]}"""),
            (inv, ct) =>
            {
                var host = runtime.Hosts.Get<IAiTerminalHost>();
                if (host is null) return HostMissing("IAiTerminalHost");
                return Wrap(host.RunSafeAsync(ReadString(inv.Input, "projectPath") ?? "", ReadString(inv.Input, "command") ?? "", ct));
            }));
    }

    // ── Task-Tools (Aufgaben-Ebene) ────────────────────────────────────────────

    private static void RegisterTaskTools(AiRuntimeService runtime, AiToolRegistry reg)
    {
        reg.Register(new AiToolDefinition
        {
            Name = "aaia.task.create", Version = "1.0.0", Since = "7.0.0",
            Description = "Erstellt eine Aufgabe (optional mit Schritten = Tool-Aufrufe).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"title":{"type":"string"},"description":{"type":"string"},"project":{"type":"string"},"steps":{"type":"array"}},"required":["title"]}"""),
            Handler = (inv, ct) =>
            {
                var title = ReadString(inv.Input, "title");
                if (string.IsNullOrWhiteSpace(title)) return Task.FromResult(AiToolResult.Fail("title fehlt.", "bad_input"));
                var steps = ParseSteps(inv.Input);
                var task = runtime.Tasks.Create(title!, ReadString(inv.Input, "description") ?? "",
                    ReadString(inv.Input, "project"), steps);
                return Task.FromResult(AiToolResult.Ok(new { taskId = task.Id, status = task.Status.ToString(), steps = task.Steps.Count }));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.task.claim", Version = "1.0.0", Since = "7.0.0",
            Description = "Übernimmt eine Aufgabe (\"Ich übernehme Aufgabe X\").",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"taskId":{"type":"string"}},"required":["taskId"]}"""),
            Handler = (inv, ct) =>
            {
                var ok = runtime.Tasks.Claim(ReadString(inv.Input, "taskId") ?? "", inv.Session, out var conflict);
                return Task.FromResult(ok
                    ? AiToolResult.Ok(new { claimed = true, owner = inv.Session.ClientName })
                    : AiToolResult.Fail(conflict ?? "Konnte nicht übernehmen.", "conflict"));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.task.list", Version = "1.0.0", Since = "7.0.0",
            Description = "Listet Aufgaben (optional projektgefiltert).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"project":{"type":"string"}}}"""),
            Handler = (inv, ct) =>
            {
                var list = runtime.Tasks.List(ReadString(inv.Input, "project"))
                    .Select(t => new { taskId = t.Id, t.Title, status = t.Status.ToString(), owner = t.OwnerClientName, steps = t.Steps.Count });
                return Task.FromResult(AiToolResult.Ok(new { tasks = list.ToArray() }));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.task.run", Version = "1.0.0", Since = "7.0.0",
            Description = "Führt die Schritte einer übernommenen Aufgabe aus (jeder Schritt durchläuft die Sicherheits-Kette).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"taskId":{"type":"string"}},"required":["taskId"]}"""),
            Handler = async (inv, ct) =>
            {
                var id = ReadString(inv.Input, "taskId") ?? "";
                if (runtime.Tasks.Get(id) is null) return AiToolResult.Fail("Aufgabe nicht gefunden.", "not_found");
                if (!runtime.Tasks.Claim(id, inv.Session, out var conflict)) return AiToolResult.Fail(conflict!, "conflict");
                var task = await runtime.Tasks.RunAsync(id, inv.Session, ct).ConfigureAwait(false);
                return AiToolResult.Ok(new
                {
                    taskId = task.Id, status = task.Status.ToString(),
                    steps = task.Steps.Select(s => new { s.ToolName, status = s.Status.ToString(), error = s.Error }).ToArray()
                });
            }
        });
    }

    // ── Workflow-Tools (kompletter Ablauf über Phasen) ─────────────────────────

    private static void RegisterWorkflowTools(AiRuntimeService runtime, AiToolRegistry reg)
    {
        reg.Register(new AiToolDefinition
        {
            Name = "aaia.workflow.standardPipeline", Version = "1.0.0", Since = "7.0.0",
            Description = "Erstellt den Standard-Ablauf Validate → Build → Package für ein Projekt.",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"projectPath":{"type":"string"}},"required":["projectPath"]}"""),
            Handler = (inv, ct) =>
            {
                var path = ReadString(inv.Input, "projectPath") ?? "";
                var wf = runtime.Workflows.Create("Standard Pipeline", path, AiWorkflowEngine.StandardPipeline(path));
                return Task.FromResult(AiToolResult.Ok(new { workflowId = wf.Id, phases = wf.Phases.Select(p => p.Name).ToArray() }));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.workflow.run", Version = "1.0.0", Since = "7.0.0",
            Description = "Führt einen Workflow phasenweise aus (jede Phase durchläuft die Sicherheits-Kette).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"workflowId":{"type":"string"}},"required":["workflowId"]}"""),
            Handler = async (inv, ct) =>
            {
                var id = ReadString(inv.Input, "workflowId") ?? "";
                if (runtime.Workflows.Get(id) is null) return AiToolResult.Fail("Workflow nicht gefunden.", "not_found");
                var wf = await runtime.Workflows.RunAsync(id, inv.Session, ct).ConfigureAwait(false);
                return AiToolResult.Ok(new
                {
                    workflowId = wf.Id, status = wf.Status.ToString(),
                    phases = wf.Phases.Select(p => new { p.Name, status = p.Status.ToString() }).ToArray()
                });
            }
        });
    }

    // ── Blackboard-Tools (gemeinsamer Speicher) ────────────────────────────────

    private static void RegisterBlackboardTools(AiRuntimeService runtime, AiToolRegistry reg)
    {
        reg.Register(new AiToolDefinition
        {
            Name = "aaia.blackboard.write", Version = "1.0.0", Since = "7.0.0",
            Description = "Schreibt aufs Blackboard (Thema, Status, Notes, Priority, Progress).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"project":{"type":"string"},"topic":{"type":"string"},"status":{"type":"string","enum":["NotStarted","InProgress","Blocked","Done"]},"notes":{"type":"string"},"priority":{"type":"string","enum":["Low","Normal","High","Critical"]},"progress":{"type":"integer"}},"required":["project","topic","status"]}"""),
            Handler = (inv, ct) =>
            {
                var project = ReadString(inv.Input, "project") ?? "";
                var topic = ReadString(inv.Input, "topic") ?? "";
                if (!Enum.TryParse<AiWorkItemStatus>(ReadString(inv.Input, "status"), true, out var status))
                    return Task.FromResult(AiToolResult.Fail("Ungültiger Status.", "bad_input"));
                AiPriority? prio = Enum.TryParse<AiPriority>(ReadString(inv.Input, "priority"), true, out var pr) ? pr : null;
                int? progress = inv.Input.TryGetProperty("progress", out var pv) && pv.ValueKind == JsonValueKind.Number ? pv.GetInt32() : null;
                var ok = runtime.Blackboard.Write(inv.Session, project, topic, status, ReadString(inv.Input, "notes"),
                    out var entry, out var conflict, prio, progress);
                return Task.FromResult(ok
                    ? AiToolResult.Ok(new { entry.Project, entry.Topic, status = entry.Status.ToString(), owner = entry.OwnerClientName, priority = entry.Priority.ToString(), entry.Progress })
                    : AiToolResult.Fail(conflict ?? "Konflikt.", "conflict"));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.blackboard.read", Version = "1.0.0", Since = "7.0.0",
            Description = "Liest das Blackboard eines Projekts (wer arbeitet woran).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"project":{"type":"string"}},"required":["project"]}"""),
            Handler = (inv, ct) =>
            {
                var items = runtime.Blackboard.List(ReadString(inv.Input, "project") ?? "")
                    .Select(i => new { i.Topic, status = i.Status.ToString(), owner = i.OwnerClientName, i.Notes, priority = i.Priority.ToString(), i.Progress, i.UpdatedAt });
                return Task.FromResult(AiToolResult.Ok(new { entries = items.ToArray() }));
            }
        });
    }

    // ── Memory-Tools (Projekt-Gedächtnis) ──────────────────────────────────────

    private static void RegisterMemoryTools(AiRuntimeService runtime, AiToolRegistry reg)
    {
        reg.Register(new AiToolDefinition
        {
            Name = "aaia.memory.record", Version = "1.0.0", Since = "7.0.0",
            Description = "Hält eine Designentscheidung/Begründung im Projekt-Gedächtnis fest.",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"project":{"type":"string"},"topic":{"type":"string"},"content":{"type":"string"},"kind":{"type":"string","enum":["Decision","Rationale","Approval","Note","Change"]},"approvedBy":{"type":"string"}},"required":["project","topic","content"]}"""),
            Handler = (inv, ct) =>
            {
                var kind = Enum.TryParse<AiMemoryKind>(ReadString(inv.Input, "kind"), true, out var k) ? k : AiMemoryKind.Decision;
                var entry = runtime.Memory.Record(inv.Session, ReadString(inv.Input, "project") ?? "",
                    ReadString(inv.Input, "topic") ?? "", ReadString(inv.Input, "content") ?? "", kind,
                    ReadString(inv.Input, "approvedBy"));
                return Task.FromResult(AiToolResult.Ok(new { entry.Id, kind = entry.Kind.ToString(), author = entry.AuthorClient, entry.TimestampUtc }));
            }
        });

        reg.Register(new AiToolDefinition
        {
            Name = "aaia.memory.query", Version = "1.0.0", Since = "7.0.0",
            Description = "Fragt das Projekt-Gedächtnis ab (warum wurde etwas so entschieden, von wem).",
            RiskLevel = AiRiskLevel.Green, RequiredPermissions = AiPermission.Read,
            InputSchema = Schema("""{"type":"object","properties":{"project":{"type":"string"},"topic":{"type":"string"}},"required":["project"]}"""),
            Handler = (inv, ct) =>
            {
                var entries = runtime.Memory.Query(ReadString(inv.Input, "project") ?? "", ReadString(inv.Input, "topic"))
                    .Select(e => new { e.Topic, kind = e.Kind.ToString(), e.Content, author = e.AuthorClient, e.ApprovedBy, e.TimestampUtc });
                return Task.FromResult(AiToolResult.Ok(new { entries = entries.ToArray() }));
            }
        });
    }

    // ── Helfer ─────────────────────────────────────────────────────────────────

    private static AiToolDefinition Core(string name, AiRiskLevel risk, AiPermission perm, bool approval,
        string description, JsonElement schema,
        Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> handler)
        => new()
        {
            Name = name, Version = "1.0.0", Since = "7.0.0", Description = description,
            RiskLevel = risk, RequiresApproval = approval, RequiredPermissions = perm,
            InputSchema = schema, Handler = handler
        };

    private static Task<AiToolResult> HostMissing(string hostInterface)
        => Task.FromResult(AiToolResult.Fail(
            $"Kein {hostInterface} registriert — wird vom Module Manager in Increment 2 bereitgestellt. Es wurde nichts verändert.",
            "host_unavailable"));

    private static async Task<AiToolResult> Wrap(Task<AiHostResult> hostCall)
    {
        var r = await hostCall.ConfigureAwait(false);
        return r.Success
            ? AiToolResult.Ok(r.Payload ?? new { })
            : AiToolResult.Fail(r.Error ?? "Host-Fehler.", r.ErrorCode ?? "host_error");
    }

    private static List<AiTaskStep> ParseSteps(JsonElement input)
    {
        var steps = new List<AiTaskStep>();
        if (input.TryGetProperty("steps", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                var tool = s.TryGetProperty("toolName", out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() : null;
                if (string.IsNullOrEmpty(tool)) continue;
                var stepInput = s.TryGetProperty("input", out var inEl) && inEl.ValueKind == JsonValueKind.Object
                    ? inEl.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
                steps.Add(new AiTaskStep { ToolName = tool!, Input = stepInput });
            }
        }
        return steps;
    }

    private static T? Bind<T>(JsonElement input)
    {
        try { return input.Deserialize<T>(JsonOpts); }
        catch { return default; }
    }

    private static string? ReadString(JsonElement input, string property)
        => input.ValueKind == JsonValueKind.Object &&
           input.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
