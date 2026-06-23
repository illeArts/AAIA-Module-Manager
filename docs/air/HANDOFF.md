# AIR — Übergabebericht (Handoff für die nächste Session)

**Stand:** Phase 7.0 vollständig abgeschlossen, **Build grün**. Schritt 2 (App-Verdrahtung) ist erledigt. Nächster Schritt: `dotnet test`, dann Stage-1-Script.

## Wo wir stehen

Implementiert und gebaut (in `src/AAIA.ModuleManager/Services/Ai/`):

- **AIR-Runtime-Kern** (`Runtime/`): `AiRuntimeService` (Orchestrator mit Sicherheits-Kette Session→Capability→Permission→Lock→Ausführung→Audit+Event), `AiSessionManager`, `AiCapabilityManager` (+ `AiClientCapabilities`), `AiPermissionEngine` (pro Client/Projekt, Default nur Read, Sign/Marketplace hart gesperrt), `AiToolRegistry` + `AiToolVersioning`, `AiWorkspaceLockService`, `AiRuntimeEventBus`, `AiAuditService`, `AiHostRegistry`.
- **Erweiterungen** (Increment 1.2): `Roles/AiRole`, `Collaboration/AiBlackboard` (+ `AiCollaborationManager`), `Tasks/AiTaskManager`, `Workflows/AiWorkflowEngine`, `Memory/AiProjectMemory`, `Providers/IAiCapabilityProvider`.
- **Host-Interfaces** (`Runtime/Hosts/`): `IAiStatusHost`, `IAiProjectHost`, `IAiFileHost`, `IAiPatchHost`, `IAiValidationHost`, `IAiBuildHost`, `IAiPackageHost`, `IAiIdeHost`, `IAiTerminalHost`, `IAiMarketplaceHost` + `AiHostResult` + DTOs. Die Runtime kennt KEINE konkrete App, nur diese Interfaces.
- **MCP-Adapter** (`Mcp/`): `AaiaMcpServer` (ASP.NET Core Streamable HTTP, 127.0.0.1:39158, Token-Middleware, SDK 1.4.0 — Handler über `WithListToolsHandler`/`WithCallToolHandler`), `AaiaMcpAdapter`, `AaiaMcpAuthHandler` (Bridge-Token, rotierbar), `AaiaMcpConfigService` (Claude+Codex Config), `AaiaMcpBridgeOptions`, `AiRuntimeComposition`.
- **Integration** (`Integration/`): `ModuleManagerHosts` (alle Host-Interfaces gegen Phase-6-Services: BuildRunnerService, ProjectValidationOrchestrator, ExtensionPackageService, IdeDetectionService, ProcessRunner), `IModuleManagerAiBridge` (App-Schnittstelle), `AiRuntimeConnectorPanel` (UI-Glue).
- **Tests** (`src/AAIA.ModuleManager.Tests/Ai/`): ~28 Tests (Foundation, Extensions, MCP-Auth/Config, MultiClient). SDK-frei.
- `AppConfig.McpBridge` ist angelegt.

## ✅ Schritt 2 — ERLEDIGT (App-Verdrahtung)

`ConnectorTabViewModel` implementiert jetzt `IModuleManagerAiBridge` (Build grün):

- `GetStatus()` — aus `_server.IsRunning`, `_airPanel?.IsRunning`, `_currentContext`, `_aaias?.IsConnected`.
- `ResolveProject()` — Pfadprüfung gegen `_projectRoot`, Mapping `AiHandoffContext.ProjectType`-String → `NewProjectType`, `.csproj`-Suche.
- `ApproveAndApplyPatchAsync()` — `AiPatchProposalInput` → `AiPatchRequest`, TCS + `ShowPatchApproval`-Callback, wartet auf `OnPatchDecision`. AIR-Proposals (Präfix `air-`) werden von HTTP-Connector-Proposals in `OnPatchDecision` getrennt.
- `CreateProjectAsync()` — mappt auf `ScaffoldOptions` → `ProjectScaffoldingService.ScaffoldAsync`.

`AiRuntimeConnectorPanel` im Konstruktor instanziiert (`new AiRuntimeConnectorPanel(config.McpBridge, this)`). `MainWindowViewModel` übergibt jetzt `TesterTab.AaiasConn`. XAML-Sektion „AAIA AIR / MCP" als aufklappbarer `Expander` mit Start/Stop/Token rotieren/Config kopieren und den 4 Statuslisten.

## NÄCHSTER SCHRITT (genau hier weitermachen)

**Schritt 3: `dotnet test`** — alle ~28 Tests müssen grün sein, bevor das Stage-1-Script läuft.

```
cd aaia-module-manager
dotnet test src/AAIA.ModuleManager.Tests/AAIA.ModuleManager.Tests.csproj
```

## Danach (Reihenfolge NICHT ändern)

4. **`scripts/migrate-air-stage1.ps1`** ausführen (extrahiert `AAIA.Air` + `AAIA.Air.Mcp`; prüft sauberen Arbeitsstand + Branch; `-BuildAfterMigration` optional). Manuelle Fixups danach: `AppConfig.cs` (`using AAIA.Air.Mcp;`, Typ `Ai.Mcp.AaiaMcpBridgeOptions`→`AaiaMcpBridgeOptions`).
5. Stage-1-Buildfehler systematisch fixen.
6. **Stage 2:** `AAIA.Air.Contracts` abspalten (reine Typen; Faustregel „Typ ohne Logik → Contracts, Manager → Air"). Inkl. neuem `AiMessage`-DTO. Quelle: `docs/air/air-platform-split.md`.
7. **Stage 3:** `AAIA.Air.SDK` (`AirToolProviderBase`/`AirToolBuilder`) für ETW-Entwickler. Quelle: ebd.
8. **Erst dann Phase 8 planen** (AI Collaboration & Orchestration).

## Harte Regeln (nicht verletzen)

- **Keine neuen AIR-Funktionen mehr in Phase 7.0.** Nur Build-/Verdrahtungsfehler lösen.
- **AIR ist herstellerneutral:** nirgends `if Claude` / `if GPT`. Nur Client / Capabilities / Permissions / Roles. (Vendor wird NICHT aus dem Namen abgeleitet.)
- **Reserviert für Phase 8, JETZT NICHT bauen:** AIR Kernel (Execution/Scheduling/Security/Memory/Messaging), Messaging-Bus, Scheduler/Execution-Queue, Resource-Manager. Nur der Datentyp `AiMessage` ist als Contract erlaubt.
- **AIR kennt keine App.** Apps (Module Manager, AAIAS, BBK, DUKI, Website, Mobile) sind Nutzer über Hosts/Contracts — nie umgekehrt.
- **Sicherheit unverändert lassen:** Bridge-Token-Pflicht, nur 127.0.0.1, Default deaktiviert, Patch nur über Approval, Terminal nur Allowlist, keine Secrets, Path-Traversal-Schutz, Black-Tools existieren nicht.

## Wo alle Informationen liegen

- `docs/phase-7.0-ai-runtime-status.md` — kompletter Implementierungsstatus (Increment 1, 1.1, 1.2, 2) + verbleibende App-Verdrahtung.
- `docs/air/air-platform-split.md` — 4-Projekt-Zielarchitektur, Datei-/Namespace-Mapping, csproj-Inhalte, Stage 2/3-Code (`AiMessage`, AIR.SDK).
- `scripts/migrate-air-stage1.ps1` — Migrations-Script Stage 1 (mit Sicherheitschecks).
- `phase-7.0-ai-runtime.md` (Repo-Wurzel `H:\AAIAGitHub\`) — die ursprüngliche, finale Spec (Runtime + MCP-Adapter, Akzeptanzkriterien).
- Quell-Code: `src/AAIA.ModuleManager/Services/Ai/{Runtime,Mcp,Integration}/`.
- Tests: `src/AAIA.ModuleManager.Tests/Ai/`.

## Namensentscheidung (fix)

Die Runtime heißt **AIR = AAIA Intelligence Runtime**, eingebettet in die **AAIA Intelligence Platform**. Code-Namespace-Ziel: `AAIA.Air` (Markenname ≠ Namespace). Der Rename `…Services.Ai.Runtime` → `AAIA.Air` passiert erst mit Stage 1 (Script), nicht vorher.
