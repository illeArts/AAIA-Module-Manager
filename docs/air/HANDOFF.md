# AIR — Übergabebericht (Handoff für die nächste Session)

**Stand:** Phase 7.0 sowie Plattform-Split Stage 1 und 2 sind abgeschlossen. Build: 0 Fehler/0 Warnungen, Tests: 112/112 grün. Nächster Schritt: Stage 3 (`AAIA.Air.SDK`).

## Wo wir stehen

Implementiert und gebaut:

- **Contracts** (`src/AAIA.Air.Contracts/`): BCL-only, keine Projekt- oder NuGet-Abhängigkeiten. Enthält öffentliche Typen, Enums, Sessions, Host-Interfaces, Task-/Workflow-Modelle, Events, Capability-Interfaces und `AiMessage`.
- **AIR-Runtime-Kern** (`src/AAIA.Air/`): `AiRuntimeService` (Orchestrator mit Sicherheits-Kette Session→Capability→Permission→Lock→Ausführung→Audit+Event), Manager, Engines, Blackboard und Memory. Referenziert nur Contracts.
- **MCP-Adapter** (`src/AAIA.Air.Mcp/`): `AaiaMcpServer` (ASP.NET Core Streamable HTTP, 127.0.0.1:39158, Token-Middleware, SDK 1.4.0), Adapter, Auth, Konfiguration und Composition.
- **Module-Manager-Integration** (`src/AAIA.ModuleManager/Services/Ai/Integration/`): app-spezifische Hosts, Bridge und UI-Glue. Die Runtime kennt den Module Manager nicht.

## ✅ Schritt 2 — ERLEDIGT (App-Verdrahtung)

`ConnectorTabViewModel` implementiert jetzt `IModuleManagerAiBridge` (Build grün):

- `GetStatus()` — aus `_server.IsRunning`, `_airPanel?.IsRunning`, `_currentContext`, `_aaias?.IsConnected`.
- `ResolveProject()` — Pfadprüfung gegen `_projectRoot`, Mapping `AiHandoffContext.ProjectType`-String → `NewProjectType`, `.csproj`-Suche.
- `ApproveAndApplyPatchAsync()` — `AiPatchProposalInput` → `AiPatchRequest`, TCS + `ShowPatchApproval`-Callback, wartet auf `OnPatchDecision`. AIR-Proposals (Präfix `air-`) werden von HTTP-Connector-Proposals in `OnPatchDecision` getrennt.
- `CreateProjectAsync()` — mappt auf `ScaffoldOptions` → `ProjectScaffoldingService.ScaffoldAsync`.

`AiRuntimeConnectorPanel` im Konstruktor instanziiert (`new AiRuntimeConnectorPanel(config.McpBridge, this)`). `MainWindowViewModel` übergibt jetzt `TesterTab.AaiasConn`. XAML-Sektion „AAIA AIR / MCP" als aufklappbarer `Expander` mit Start/Stop/Token rotieren/Config kopieren und den 4 Statuslisten.

## NÄCHSTER SCHRITT (genau hier weitermachen)

1. **Stage 3:** `AAIA.Air.SDK` mit `AirToolProviderBase` und `AirToolBuilder` für ETW-Entwickler erstellen.
2. SDK gegen `AAIA.Air.Contracts` referenzieren; keine Abhängigkeit auf Runtime, MCP oder Module Manager.
3. Builder-/Provider-Tests ergänzen und kompletten Build/Test erneut ausführen.
4. Danach Plattform-Split als abgeschlossen sichern und erst dann Phase 8 planen.

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
- Quell-Code: `src/AAIA.Air.Contracts/`, `src/AAIA.Air/`, `src/AAIA.Air.Mcp/` sowie `src/AAIA.ModuleManager/Services/Ai/Integration/`.
- Tests: `src/AAIA.ModuleManager.Tests/Ai/`.

## Namensentscheidung (fix)

Die Runtime heißt **AIR = AAIA Intelligence Runtime**, eingebettet in die **AAIA Intelligence Platform**. Die Ziel-Namespaces `AAIA.Air.Contracts`, `AAIA.Air` und `AAIA.Air.Mcp` sind umgesetzt.
