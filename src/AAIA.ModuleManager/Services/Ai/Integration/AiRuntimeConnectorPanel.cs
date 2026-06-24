using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAIA.Air.Mcp;
using AAIA.Air;

namespace AAIA.ModuleManager.Services.Ai.Integration;

/// <summary>
/// UI-Glue für den Connector-Tab (Sektion „AAIA AIR / MCP"). Setzt die AIR mit den
/// echten Module-Manager-Hosts zusammen und stellt Start/Stop, Token-Rotation,
/// Client-Config und Statusanzeigen bereit. Bewusst framework-neutral (kein MVVM-Base),
/// damit das ViewModel nur Methoden/Properties bindet.
/// </summary>
public sealed class AiRuntimeConnectorPanel
{
    private readonly AiRuntimeComposition _composition;

    public AiRuntimeConnectorPanel(AaiaMcpBridgeOptions options, IModuleManagerAiBridge bridge)
    {
        var hosts = new ModuleManagerHosts(bridge);
        _composition = new AiRuntimeComposition(options, hosts);

        _composition.Server.Log += msg => Log?.Invoke(msg);
        _composition.Runtime.Events.EventPublished += e => LastEvent =
            $"{e.TimestampUtc:HH:mm:ss} {e.Type} {e.Tool} ({e.ClientName})";
    }

    public event Action<string>? Log;

    public bool IsRunning => _composition.Server.IsRunning;
    public int  Port      => _composition.Server.Port;
    public string Url      => _composition.Server.Url;
    public string? LastEvent { get; private set; }

    // ── Bridge-Steuerung ─────────────────────────────────────────────────────
    public Task StartAsync(CancellationToken ct = default) => _composition.Server.StartAsync(ct);
    public Task StopAsync(CancellationToken ct = default)  => _composition.Server.StopAsync(ct);
    public string RotateToken() => _composition.Auth.Rotate();

    // ── Client-Config (inkl. aktuellem Token) ────────────────────────────────
    public string ClaudeDesktopConfig() => _composition.Config.ClaudeDesktopJson();
    public string CodexConfig()         => _composition.Config.CodexToml();

    // ── Explizite Phase-8-MCP-Freigaben (Default AUS) ──────────────────────
    public bool AllowCollaboration => _composition.Options.AllowCollaboration;
    public bool AllowScheduling    => _composition.Options.AllowScheduling;
    public bool AllowResourceRead  => _composition.Options.AllowResourceRead;

    public void SetPhase8McpAccess(bool collaboration, bool scheduling, bool resourceRead)
    {
        _composition.Options.AllowCollaboration = collaboration;
        _composition.Options.AllowScheduling = scheduling;
        _composition.Options.AllowResourceRead = resourceRead;
        _composition.Adapter.ApplyPhase8OptionsToActiveSessions();
    }

    // ── Statusanzeigen für die UI ────────────────────────────────────────────
    public IReadOnlyList<string> ActiveSessions()
        => _composition.Runtime.Sessions.Active
            .Select(s => $"{s.Identity} · {s.CurrentProject ?? "—"} · {s.GrantedPermissions}")
            .ToList();

    public IReadOnlyList<string> ActiveLocks()
        => _composition.Runtime.Locks.Active
            .Select(l => $"{l.Scope} {l.NormalizedPath} (Session {l.OwnerSessionId})")
            .ToList();

    public IReadOnlyList<string> ActiveTools()
        => _composition.Runtime.Tools.ListActive().Select(t => $"{t.Name} v{t.Version} [{t.RiskLevel}]").ToList();

    public IReadOnlyList<string> RecentAudit(int n = 50)
        => _composition.Runtime.Audit.Recent(n)
            .Select(a => $"{a.TimestampUtc:HH:mm:ss} {a.ClientIdentity} {a.Tool} → {(a.Success ? "ok" : "FAIL")}")
            .ToList();

    public IReadOnlyList<string> MessageInboxes()
        => _composition.Runtime.Sessions.Active
            .Select(session =>
            {
                _composition.Runtime.Messages.TryReadInbox(session.SessionId, false, out var all);
                var unread = all.Count(message => !message.IsAcknowledged);
                return $"{session.ClientName} · {unread} ungelesen / {all.Count} gesamt";
            })
            .Take(100)
            .ToList();

    public IReadOnlyList<string> Executions()
        => _composition.Runtime.Scheduler.List()
            .TakeLast(100)
            .Select(item => $"{item.Request.Id} · {item.State} · Task {item.Request.TaskId} · {item.Request.Priority}")
            .ToList();

    public IReadOnlyList<string> Resources()
    {
        try { _composition.RefreshResourceHost(); }
        catch (Exception ex) { Log?.Invoke($"Resource-Host konnte nicht aktualisiert werden: {ex.Message}"); }

        var active = _composition.Runtime.Resources.ListReservations()
            .Where(item => item.State == AiReservationState.Reserved)
            .GroupBy(item => item.ResourceId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return _composition.Runtime.Resources.Registry.ListProfiles()
            .Take(100)
            .Select(profile =>
            {
                var telemetry = _composition.Runtime.Resources.Registry.GetTelemetry(profile.ResourceId);
                var health = telemetry is null ? "ohne Telemetrie" : telemetry.Healthy && !telemetry.Throttled ? "gesund" : "eingeschränkt";
                return $"{profile.ResourceId} · {profile.Kind} · {health} · {active.GetValueOrDefault(profile.ResourceId)} reserviert";
            })
            .ToList();
    }

    // ── Lokale, bestätigte Admin-Aktionen; niemals über MCP aufrufbar ───────
    public bool TryCancelExecution(
        string executionId, string actor, string reason, bool isAdministrator, bool confirmed, out string? error)
    {
        if (!AuthorizeAdministrativeAction(
                actor, reason, isAdministrator, confirmed, "air.admin.execution.cancel", out error)) return false;
        var execution = _composition.Runtime.Scheduler.Get(executionId);
        if (execution is null)
            return AuditFailure(actor, "air.admin.execution.cancel", "Execution nicht gefunden.", out error);
        var success = _composition.Runtime.Scheduler.Cancel(executionId);
        var detail = $"Execution {executionId}: {reason}";
        _composition.Runtime.Audit.RecordAdministrative(actor, "air.admin.execution.cancel", success, detail);
        error = success ? null : "Execution ist bereits abgeschlossen.";
        return success;
    }

    public bool TrySetResourceEnabled(
        string resourceId, bool enabled, string actor, string reason,
        bool isAdministrator, bool confirmed, out string? error)
    {
        if (!AuthorizeAdministrativeAction(
                actor, reason, isAdministrator, confirmed, "air.admin.resource.enabled", out error)) return false;
        var success = _composition.Runtime.Resources.Registry.SetEnabled(resourceId, enabled);
        var detail = $"Resource {resourceId} Enabled={enabled}: {reason}";
        _composition.Runtime.Audit.RecordAdministrative(actor, "air.admin.resource.enabled", success, detail);
        error = success ? null : "Ressource nicht gefunden.";
        return success;
    }

    public bool TryCreateBudget(
        AiResourceBudget budget, string actor, string reason,
        bool isAdministrator, bool confirmed, out string? error)
    {
        if (!AuthorizeAdministrativeAction(
                actor, reason, isAdministrator, confirmed, "air.admin.resource.budget", out error)) return false;
        try
        {
            _composition.Runtime.Resources.SetBudget(budget);
            _composition.Runtime.Audit.RecordAdministrative(
                actor, "air.admin.resource.budget", true,
                $"Budget {budget.Id} Scope={budget.Scope}: {reason}");
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _composition.Runtime.Audit.RecordAdministrative(
                actor, "air.admin.resource.budget", false, $"Budget abgelehnt: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    private bool AuditFailure(string actor, string action, string message, out string? error)
    {
        _composition.Runtime.Audit.RecordAdministrative(actor, action, false, message);
        error = message;
        return false;
    }

    private bool AuthorizeAdministrativeAction(
        string actor, string reason, bool isAdministrator, bool confirmed, string action, out string? error)
    {
        if (!isAdministrator)
            error = "Lokale Administratorrechte erforderlich.";
        else if (!confirmed)
            error = "Aktion wurde nicht bestätigt.";
        else if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            error = "Akteur und Begründung (maximal 500 Zeichen) sind erforderlich.";
        else
        {
            error = null;
            return true;
        }

        _composition.Runtime.Audit.RecordAdministrative(
            string.IsNullOrWhiteSpace(actor) ? "local-unknown" : actor,
            action,
            false,
            error);
        return false;
    }

    /// <summary>Direkter Zugriff auf die Runtime (z. B. für erweiterte UI-Panels).</summary>
    public AiRuntimeService Runtime => _composition.Runtime;
}
