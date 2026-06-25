using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.Air.Collaboration;
using AAIA.Air.Hosts;
using AAIA.Air.Memory;
using AAIA.Air.Messaging;
using AAIA.Air.Providers;
using AAIA.Air.Persistence;
using AAIA.Air.Scheduling;
using AAIA.Air.Resources;
using AAIA.Air.Tasks;
using AAIA.Air.Workflows;

namespace AAIA.Air;

/// <summary>
/// Zentraler Orchestrator der AI Runtime. Jeder Adapter (MCP heute, REST/Internal
/// später) ruft denselben Einstiegspunkt. Kein Adapter umgeht diese Kette:
/// Session gültig → Capability → Permission → Tool aktiv/nicht deprecated →
/// Workspace-Lock → Ausführung über Phase-6-Service → Audit + Event.
/// </summary>
public sealed class AiRuntimeService
{
    private IAiRuntimeMutationPersistence? _mutationPersistence;
    private Func<bool>? _readinessGate;
    public AiToolRegistry        Tools         { get; }
    public AiSessionManager      Sessions      { get; }
    public AiCapabilityManager   Capabilities  { get; }
    public AiPermissionEngine    Permissions   { get; }
    public AiWorkspaceLockService Locks        { get; }
    public AiRuntimeEventBus     Events        { get; }
    public AiAuditService        Audit         { get; }

    /// <summary>Stabil clientgebundene, begrenzte Idempotenz für mutierende Adapteraufrufe.</summary>
    public AiIdempotencyStore    Idempotency   { get; } = new();

    /// <summary>Interne Ressourcenwahl für Kapazität, Last und Budgets.</summary>
    public AiResourceManager     Resources     { get; }

    /// <summary>Sessiongebundene Nachrichten zwischen AIR-Teilnehmern.</summary>
    public AiMessageBus          Messages      { get; }

    /// <summary>Host-Registry — die Runtime kennt nur Interfaces, keine konkrete App.</summary>
    public AiHostRegistry        Hosts         { get; } = new();

    /// <summary>Aufgaben-Ebene über den Tool-Calls (eine Aufgabe).</summary>
    public AiTaskManager         Tasks         { get; }

    /// <summary>Priorisierte Execution Queue mit Rollen-/Capability-Matching.</summary>
    public AiExecutionScheduler  Scheduler     { get; }

    /// <summary>Workflow-Ebene über mehreren Phasen (kompletter Ablauf).</summary>
    public AiWorkflowEngine      Workflows     { get; }

    /// <summary>Blackboard — gemeinsamer Speicher für koordinierte Mehr-KI-Arbeit.</summary>
    public AiBlackboard          Blackboard    { get; } = new();

    /// <summary>Projekt-Gedächtnis (Designentscheidungen, Zustimmungen, wer/wann).</summary>
    public AiProjectMemory       Memory        { get; } = new();

    /// <summary>Koordiniert Zusammenarbeit (wer arbeitet woran, Review, Test).</summary>
    public AiCollaborationManager Collaboration { get; }

    /// <summary>Von Modulen deklarierte externe Fähigkeiten (Filesystem, Scanner, …).</summary>
    public AiCapabilityRequirementRegistry CapabilityRequirements { get; } = new();

    /// <summary>Runtime-Version (für Tool.Since/Versionierung).</summary>
    public string RuntimeVersion { get; } = "7.0.0";

    public AiRuntimeRecoveryStatus PersistenceStatus
        => _mutationPersistence?.Status ?? AiRuntimeRecoveryStatus.Disabled;

    public AiRuntimeService(
        AiToolRegistry tools,
        AiSessionManager sessions,
        AiCapabilityManager capabilities,
        AiPermissionEngine permissions,
        AiWorkspaceLockService locks,
        AiRuntimeEventBus events,
        AiAuditService audit)
    {
        Tools        = tools;
        Sessions     = sessions;
        Capabilities = capabilities;
        Permissions  = permissions;
        Locks        = locks;
        Events       = events;
        Audit        = audit;
        Messages     = new AiMessageBus(Sessions, Events);
        Resources    = new AiResourceManager(events: Events);

        // Task- und Workflow-Schritte laufen durch denselben sicheren Runtime-Pfad.
        Func<string, string, JsonElement, CancellationToken, Task<(bool, string)>> executor =
            async (sid, tool, input, ct) =>
            {
                var r = await InvokeToolAsync(sid, tool, input, ct).ConfigureAwait(false);
                var json = r.Success ? r.Payload.GetRawText()
                                     : JsonSerializer.Serialize(new { error = r.Error, code = r.ErrorCode });
                return (r.Success, json);
            };

        Tasks         = new AiTaskManager    { Executor = executor };
        Scheduler     = new AiExecutionScheduler(Tasks, Sessions, Events, resources: Resources);
        Workflows     = new AiWorkflowEngine { Executor = executor };
        Collaboration = new AiCollaborationManager(Sessions, Blackboard);
    }

    /// <summary>
    /// Führt ein Tool im Namen einer Session aus. Wird von JEDEM Adapter aufgerufen.
    /// </summary>
    public async Task<AiToolResult> InvokeToolAsync(
        string sessionId, string toolName, JsonElement input, CancellationToken ct = default)
    {
        // 1. Session gültig?
        if (!Sessions.TryGet(sessionId, out var session))
            return AiToolResult.Fail("Ungültige oder abgelaufene Session.", "no_session");
        Sessions.Touch(sessionId);

        // 2. Tool existiert und aktiv?
        var tool = Tools.Resolve(toolName);
        if (tool is null)
            return Reject(session, toolName, "Unbekanntes Tool.", "unknown_tool");
        if (!tool.IsActive)
            return Reject(session, toolName, "Tool ist deaktiviert.", "tool_disabled");
        if (tool.RiskLevel == AiRiskLevel.Black)
            return Reject(session, toolName, "Black-Tools existieren nicht.", "black_blocked");
        var durableMutation = _mutationPersistence?.IsDurableMutation(tool) == true;
        if (durableMutation && _readinessGate is not null && !_readinessGate())
            return Reject(session, toolName,
                "Runtime-Readiness-Lease ist abgelaufen oder nicht mehr gültig.",
                AiRuntimeStateReasonCodes.ReadinessExpired);
        if (durableMutation && _mutationPersistence!.Status is not
            (AiRuntimeRecoveryStatus.Ready or AiRuntimeRecoveryStatus.Disabled))
        {
            var code = _mutationPersistence.Status == AiRuntimeRecoveryStatus.RecoveryFailed
                ? "runtime_recovery_failed"
                : "runtime_recovering";
            return Reject(session, toolName,
                "Runtime-Persistenz ist noch nicht für Mutationen freigegeben.", code);
        }

        // 3. Capability Negotiation
        if (!Capabilities.Supports(session, tool))
            return Reject(session, tool.Name, "Client besitzt nicht die nötigen Capabilities.", "capability_missing");

        // 4. Permission (pro Client/Projekt)
        var projectPath = TryReadString(input, "projectPath");
        if (!Permissions.IsAllowed(session, tool, projectPath, out var reason))
            return Reject(session, tool.Name, reason, "permission_denied");

        // 5. Workspace-Lock für schreibende Tools
        AiWorkspaceLock? acquiredLock = null;
        if (NeedsWriteLock(tool) && !string.IsNullOrEmpty(projectPath))
        {
            if (!Locks.TryAcquire(session, AiLockScope.Project, projectPath!, out acquiredLock, out var conflict))
                return Reject(session, tool.Name, $"Workspace gesperrt: {conflict}", "locked");
        }

        // 6. Ausführung (Tool ruft Phase-6-Service; Approval passiert im Handler)
        try
        {
            session.CurrentProject = projectPath ?? session.CurrentProject;
            Events.Publish(AiRuntimeEventType.ToolCalled, session, tool.Name);

            var invocation = new AiToolInvocation { Session = session, ToolName = tool.Name, Input = input };
            var result = await tool.Handler(invocation, ct).ConfigureAwait(false);

            Audit.Record(session, tool, result.Success, result.Success ? null : result.Error);
            if (result.Success && durableMutation &&
                _mutationPersistence!.Status != AiRuntimeRecoveryStatus.Disabled)
            {
                try
                {
                    await _mutationPersistence.PersistMutationAsync(tool.Name, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is AiStateStoreException or IOException or UnauthorizedAccessException)
                {
                    Audit.Record(session, tool, false, $"Persistenz fehlgeschlagen: {ex.Message}");
                    Events.Publish(AiRuntimeEventType.ErrorOccurred, session, tool.Name,
                        "Durable Bestätigung fehlgeschlagen.");
                    return AiToolResult.Fail(
                        "Mutation konnte nicht dauerhaft bestätigt werden.",
                        AiRuntimeStateReasonCodes.PersistenceFailed);
                }
            }
            if (!result.Success)
                Events.Publish(AiRuntimeEventType.ErrorOccurred, session, tool.Name, result.Error);
            return result;
        }
        catch (OperationCanceledException)
        {
            Audit.Record(session, tool, false, "abgebrochen");
            return AiToolResult.Fail("Tool-Ausführung abgebrochen (Timeout/Cancel).", "cancelled");
        }
        catch (AiStateStoreException ex)
        {
            Audit.Record(session, tool, false, $"Persistenz fehlgeschlagen: {ex.Message}");
            Events.Publish(AiRuntimeEventType.ErrorOccurred, session, tool.Name,
                "Durable Bestätigung fehlgeschlagen.");
            return AiToolResult.Fail(
                "Mutation konnte nicht dauerhaft bestätigt werden.",
                AiRuntimeStateReasonCodes.PersistenceFailed);
        }
        catch (Exception ex)
        {
            Audit.Record(session, tool, false, ex.Message);
            Events.Publish(AiRuntimeEventType.ErrorOccurred, session, tool.Name, ex.Message);
            return AiToolResult.Fail("Interner Fehler bei der Tool-Ausführung.", "internal_error");
        }
        finally
        {
            if (acquiredLock is not null)
                Locks.Release(session, acquiredLock.LockId);
        }
    }

    private AiToolResult Reject(AiSession session, string toolName, string reason, string code)
    {
        Events.Publish(new AiRuntimeEvent
        {
            Type = AiRuntimeEventType.ErrorOccurred,
            SessionId = session.SessionId,
            ClientName = session.ClientName,
            Tool = toolName,
            Message = reason
        });
        return AiToolResult.Fail(reason, code);
    }

    private static bool NeedsWriteLock(AiToolDefinition tool)
        => (tool.RequiredPermissions &
            (AiPermission.ProposePatch | AiPermission.Build | AiPermission.Package)) != 0;

    internal void AttachMutationPersistence(IAiRuntimeMutationPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        if (_mutationPersistence is not null && !ReferenceEquals(_mutationPersistence, persistence))
            throw new InvalidOperationException("Runtime-Persistenz ist bereits konfiguriert.");
        _mutationPersistence = persistence;
    }

    public void AttachReadinessGate(Func<bool>? readinessGate)
        => _readinessGate = readinessGate;

    private static string? TryReadString(JsonElement input, string property)
    {
        if (input.ValueKind == JsonValueKind.Object &&
            input.TryGetProperty(property, out var v) &&
            v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }
}
