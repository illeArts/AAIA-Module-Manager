# AAIA Intelligence Platform — 4-Projekt-Split (Migrations-Guide)

Ziel-Architektur:

```
AAIA Intelligence Platform
        │
        ▼
AAIA Intelligence Runtime (AIR)
 ├── AAIA.Air.Contracts   (nur Typen/Interfaces/Events — keine Logik, keine Abhängigkeiten)
 ├── AAIA.Air             (Runtime: Sessions, Rollen, Tasks, Workflow, Blackboard, Memory, …)
 ├── AAIA.Air.Mcp         (MCP-Adapter auf dem offiziellen C# MCP SDK)
 └── AAIA.Air.SDK         (öffentliches ETW-Entwicklerpaket: IAiToolProvider etc.)

Genutzt von: Module Manager, AAIAS, BBK, DUKI, Website, Mobile — die referenzieren
nur noch Contracts (+ bei Bedarf Air/Air.Mcp). AIR nutzt NIE eine App.
```

**Reihenfolge (wichtig):** Erst lokal grün bauen (Phase 7.0 Increment 1–2), dann diesen Split. Alles in einem `git`-Branch, jederzeit reversibel.

## Projekt-Verantwortlichkeiten

| Projekt | Abhängigkeiten | Inhalt |
|---|---|---|
| `AAIA.Air.Contracts` | keine (nur BCL) | Enums (`AiRiskLevel`, `AiPermission`, `AiLockScope`, `AiRuntimeEventType`, `AiCapabilities`), `AiClientIdentity`, `AiSession`, `AiClientCapabilities`, `AiRole`, `AiToolDefinition`/`AiToolResult`/`AiToolInvocation`, `IAiToolProvider`, `IAiCapabilityProvider`, alle Host-Interfaces + `AiHostResult` + DTOs, `AiTask`/`AiWorkflow`-Modelle, `AiMessage`, `AiRuntimeEvent` |
| `AAIA.Air` | Contracts | Manager/Engines: `AiRuntimeService`, `AiSessionManager`, `AiCapabilityManager`, `AiPermissionEngine`, `AiToolRegistry`, `AiToolVersioning`, `AiWorkspaceLockService`, `AiRuntimeEventBus`, `AiAuditService`, `AiHostRegistry`, `AiTaskManager`, `AiWorkflowEngine`, `AiBlackboard`, `AiCollaborationManager`, `AiProjectMemory`, `AaiaCoreToolsBootstrap` |
| `AAIA.Air.Mcp` | Air, Contracts, MCP-SDK, ASP.NET Core | `AaiaMcpServer`, `AaiaMcpAdapter`, `AaiaMcpAuthHandler`, `AaiaMcpConfigService`, `AaiaMcpBridgeOptions`, `AiRuntimeComposition` |
| `AAIA.Air.SDK` | Contracts | öffentliche ETW-Oberfläche: Re-Exports, `AirToolProviderBase`, Beispiele |
| `AAIA.ModuleManager` | Air, Air.Mcp (+ SDK) | nur noch `Services/Ai/Integration/*` (Hosts-Implementierung, Bridge, UI-Panel) |

## Stage 1 — mechanisch (Script)

`scripts/migrate-air-stage1.ps1` macht den Großteil automatisch. Vorbedingungen, die das Script selbst hart prüft:

- **Sauberer Arbeitsstand** (`git status --porcelain` muss leer sein) — sonst Abbruch, damit nie in einen schmutzigen Working Tree verschoben wird.
- **Branch `air-extraction` darf nicht existieren** — sonst Abbruch statt blindem `checkout -b`.
- Optional `-BuildAfterMigration`: führt direkt `dotnet build` aus (ein Fehlschlag ist nach Stage 1 erwartbar, solange die manuellen Fixups offen sind).

Ablauf:

1. Branch `air-extraction`.
2. Verschiebt `Services/Ai/Runtime/**` → `src/AAIA.Air/` (außer `AiRuntimeComposition.cs` → `AAIA.Air.Mcp`).
3. Verschiebt `Services/Ai/Mcp/**` → `src/AAIA.Air.Mcp/`.
4. Namespace-Ersetzung: `AAIA.ModuleManager.Services.Ai.Runtime` → `AAIA.Air`, `…Ai.Mcp` → `AAIA.Air.Mcp` (inkl. Subnamespaces, z. B. `…Runtime.Hosts` → `AAIA.Air.Hosts`).
5. Module-Manager-csproj: MCP-SDK-PackageRefs raus, ProjectRefs auf Air/Air.Mcp rein.
6. `dotnet sln add` für beide neuen Projekte.

Danach: `dotnet build`.

### Manuelle Fixups nach Stage 1

- **`AppConfig.cs`**: `using AAIA.Air.Mcp;` ergänzen; Property-Typ `Ai.Mcp.AaiaMcpBridgeOptions` → `AaiaMcpBridgeOptions` (das Skript ersetzt nur voll qualifizierte Namespaces, nicht den relativen `Ai.Mcp.`-Zugriff).
- **`Services/Ai/Integration/*`** bleiben im Module Manager; ihre `using`-Direktiven wurden vom Skript auf `AAIA.Air*` umgestellt — prüfen.
- **SDK-Naht** `AAIA.Air.Mcp/AaiaMcpServer.cs`: Tool-/CallToolResult-Symbole gegen die installierte MCP-SDK-Version abgleichen.

Wenn Build + Tests grün sind, ist die Runtime ein eigenständiges Paket. Stage 2 ist optional, aber empfohlen.

## Stage 2 — Contracts abspalten (manuell, kleiner Schritt)

Lege `src/AAIA.Air.Contracts/AAIA.Air.Contracts.csproj` an (net8.0, keine Abhängigkeiten) und verschiebe die **reinen Typdateien** aus `AAIA.Air` dorthin, Namespace `AAIA.Air.Contracts`:

```
AiRuntimePrimitives.cs        (Enums + AiCapabilities)
AiClientIdentity.cs
AiClientCapabilities.cs
AiSession.cs
Roles/AiRole.cs
AiToolModels.cs               (AiToolDefinition, AiToolInvocation, AiToolResult, IAiToolProvider)
Hosts/AiHostInterfaces.cs     (Interfaces + AiHostResult + DTOs)
Tasks/AiTask.cs               (nur Modell)
Workflows/AiWorkflow.cs       (nur Modell)
Providers/IAiCapabilityProvider.cs   (Interface; Registry bleibt in Air)
AiMessage.cs                  (neu, s. u.)
```

`AAIA.Air` referenziert dann `AAIA.Air.Contracts`. Faustregel: **Typ ohne Logik → Contracts; Manager/Engine → Air.** Den Compiler entscheiden lassen, welche `using AAIA.Air.Contracts;` nötig sind.

`AiMessage` ist ein neuer Contract-Typ (Basis für AIR Messaging in Phase 8 — hier nur der Datentyp, keine Logik):

```csharp
namespace AAIA.Air.Contracts;

public enum AiMessagePriority { Low, Normal, High, Urgent }

/// <summary>
/// Nachricht zwischen Teilnehmern der AIR (KI ↔ KI ↔ Mensch), zugestellt über AIR.
/// Reiner Datentyp — der Messaging-Bus selbst kommt in Phase 8.
/// </summary>
public sealed class AiMessage
{
    public string Id { get; } = System.Guid.NewGuid().ToString("N")[..12];
    public required string Sender { get; init; }
    public required string Receiver { get; init; }     // SessionId/ClientId oder "broadcast"
    public string Subject { get; init; } = "";
    public string Payload { get; init; } = "";
    public AiMessagePriority Priority { get; init; } = AiMessagePriority.Normal;
    public string? CorrelationId { get; init; }         // verknüpft Anfrage/Antwort
    public System.DateTime TimestampUtc { get; } = System.DateTime.UtcNow;
}
```

## Stage 3 — AIR.SDK für ETW-Entwickler

`src/AAIA.Air.SDK/AAIA.Air.SDK.csproj` (net8.0, ProjectReference auf Contracts; später als NuGet `AAIA.Air.SDK`). Ziel: ein Modulentwickler schreibt

```csharp
public sealed class FritzTools : AirToolProviderBase
{
    public override string ProviderId => "fritzbox";
    protected override void Configure(AirToolBuilder tools)
    {
        tools.Add("fritz.status",  "FRITZ!Box-Status",     AiRiskLevel.Green,  StatusAsync);
        tools.Add("fritz.devices", "Verbundene Geräte",    AiRiskLevel.Green,  DevicesAsync);
        tools.Add("fritz.logs",    "Ereignisprotokoll",    AiRiskLevel.Green,  LogsAsync);
    }
    // … StatusAsync/DevicesAsync/LogsAsync …
}
```

und referenziert nur `dotnet add package AAIA.Air.SDK`. `AirToolProviderBase` + `AirToolBuilder` kapseln die `IAiToolProvider`/`AiToolDefinition`-Erzeugung, sodass ETWs nie das interne Tool-Schema von Hand bauen müssen. Skizze:

```csharp
namespace AAIA.Air.SDK;

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AAIA.Air.Contracts;

public abstract class AirToolProviderBase : IAiToolProvider
{
    public abstract string ProviderId { get; }
    protected abstract void Configure(AirToolBuilder tools);

    public IEnumerable<AiToolDefinition> GetTools()
    {
        var b = new AirToolBuilder();
        Configure(b);
        return b.Build();
    }
}

public sealed class AirToolBuilder
{
    private readonly List<AiToolDefinition> _tools = new();
    private static readonly JsonElement EmptyObject =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    public AirToolBuilder Add(string name, string description, AiRiskLevel risk,
        System.Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> handler,
        AiPermission permissions = AiPermission.Read, JsonElement? inputSchema = null)
    {
        _tools.Add(new AiToolDefinition
        {
            Name = name, Description = description, RiskLevel = risk,
            RequiredPermissions = permissions, InputSchema = inputSchema ?? EmptyObject,
            Handler = handler
        });
        return this;
    }

    public IReadOnlyList<AiToolDefinition> Build() => _tools;
}
```

Damit registriert sich ein Modul über `runtime.Tools.RegisterProvider(new FritzTools())` — der AIR-Kern kennt das Modul nicht.

## Reserviert für Phase 8 (NICHT jetzt bauen)

Diese Namespaces/Ordner können in `AAIA.Air` leer reserviert werden, bleiben aber bis Phase 8 ohne Implementierung:

```
AAIA.Air.Kernel        (Execution, Scheduling, Security, Memory, Messaging — koordinierender Unterbau)
AAIA.Air.Messaging     (Bus für AiMessage: Sender/Empfänger/Priority/CorrelationId)
AAIA.Air.Scheduling    (Execution Queue: welche KI bekommt wann welchen Task)
AAIA.Air.Resources     (Resource Manager: Kosten/CPU — welches Modell für welche Aufgabe)
```

Begründung für die Reservierung: sobald Phase 8/9 darauf aufsetzen, ändert sich der Kernel nicht mehr. Der Datentyp `AiMessage` steht bereits in Contracts bereit.

## Endzustand

```
AAIA.Air.Contracts  ← referenziert von allen
AAIA.Air            → Contracts
AAIA.Air.Mcp        → Air, Contracts, MCP-SDK
AAIA.Air.SDK        → Contracts
AAIA.ModuleManager  → Air, Air.Mcp, SDK   (nur noch Integration/*)
AAIAS / BBK / DUKI / Website / Mobile → Contracts (+ optional Air/Air.Mcp)
```

Der Module Manager ist damit ein **Nutzer** der AIR, nicht ihr Zentrum.
