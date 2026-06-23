using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Ai.Mcp;
using AAIA.ModuleManager.Services.Ai.Runtime;

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

    /// <summary>Direkter Zugriff auf die Runtime (z. B. für erweiterte UI-Panels).</summary>
    public AiRuntimeService Runtime => _composition.Runtime;
}
