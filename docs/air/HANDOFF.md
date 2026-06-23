# AIR — Übergabebericht (Handoff für die nächste Session)

**Stand:** Phase 7.0, Plattform-Split sowie Phase 8.1 Messaging und 8.2 Scheduling sind abgeschlossen. Tests: 135/135 grün. Phase 8.3 Resource Manager ist spezifiziert, aber nicht implementiert.

## Wo wir stehen

Implementiert und gebaut:

- **Contracts** (`src/AAIA.Air.Contracts/`): BCL-only, keine Projekt- oder NuGet-Abhängigkeiten. Enthält öffentliche Typen, Enums, Sessions, Host-Interfaces, Task-/Workflow-Modelle, Events, Capability-Interfaces und `AiMessage`.
- **AIR-Runtime-Kern** (`src/AAIA.Air/`): `AiRuntimeService` (Orchestrator mit Sicherheits-Kette Session→Capability→Permission→Lock→Ausführung→Audit+Event), Manager, Engines, Blackboard und Memory. Referenziert nur Contracts.
- **MCP-Adapter** (`src/AAIA.Air.Mcp/`): `AaiaMcpServer` (ASP.NET Core Streamable HTTP, 127.0.0.1:39158, Token-Middleware, SDK 1.4.0), Adapter, Auth, Konfiguration und Composition.
- **SDK** (`src/AAIA.Air.SDK/`): öffentliche ETW-Entwickleroberfläche mit `AirToolProviderBase` und `AirToolBuilder`; referenziert ausschließlich Contracts.
- **Module-Manager-Integration** (`src/AAIA.ModuleManager/Services/Ai/Integration/`): app-spezifische Hosts, Bridge und UI-Glue. Die Runtime kennt den Module Manager nicht.

## ✅ Schritt 2 — ERLEDIGT (App-Verdrahtung)

`ConnectorTabViewModel` implementiert jetzt `IModuleManagerAiBridge` (Build grün):

- `GetStatus()` — aus `_server.IsRunning`, `_airPanel?.IsRunning`, `_currentContext`, `_aaias?.IsConnected`.
- `ResolveProject()` — Pfadprüfung gegen `_projectRoot`, Mapping `AiHandoffContext.ProjectType`-String → `NewProjectType`, `.csproj`-Suche.
- `ApproveAndApplyPatchAsync()` — `AiPatchProposalInput` → `AiPatchRequest`, TCS + `ShowPatchApproval`-Callback, wartet auf `OnPatchDecision`. AIR-Proposals (Präfix `air-`) werden von HTTP-Connector-Proposals in `OnPatchDecision` getrennt.
- `CreateProjectAsync()` — mappt auf `ScaffoldOptions` → `ProjectScaffoldingService.ScaffoldAsync`.

`AiRuntimeConnectorPanel` im Konstruktor instanziiert (`new AiRuntimeConnectorPanel(config.McpBridge, this)`). `MainWindowViewModel` übergibt jetzt `TesterTab.AaiasConn`. XAML-Sektion „AAIA AIR / MCP" als aufklappbarer `Expander` mit Start/Stop/Token rotieren/Config kopieren und den 4 Statuslisten.

## NÄCHSTER SCHRITT (genau hier weitermachen)

1. Phase-8.3-Spezifikation prüfen und fachlich freigeben.
2. Erst danach die dokumentierte Implementierungsreihenfolge beginnen.
3. MCP, UI und Host-Adapter bleiben vollständig außerhalb von Phase 8.3.

## Harte Regeln (nicht verletzen)

- **Keine neuen AIR-Funktionen mehr in Phase 7.0.** Nur Build-/Verdrahtungsfehler lösen.
- **AIR ist herstellerneutral:** nirgends `if Claude` / `if GPT`. Nur Client / Capabilities / Permissions / Roles. (Vendor wird NICHT aus dem Namen abgeleitet.)
- **Phase 8 inkrementell halten:** Messaging und Scheduler sind umgesetzt; Resource Manager und MCP-Freigabe bleiben getrennte, jeweils eigenständig geprüfte Änderungen.
- **AIR kennt keine App.** Apps (Module Manager, AAIAS, BBK, DUKI, Website, Mobile) sind Nutzer über Hosts/Contracts — nie umgekehrt.
- **Sicherheit unverändert lassen:** Bridge-Token-Pflicht, nur 127.0.0.1, Default deaktiviert, Patch nur über Approval, Terminal nur Allowlist, keine Secrets, Path-Traversal-Schutz, Black-Tools existieren nicht.

## Wo alle Informationen liegen

- `docs/phase-7.0-ai-runtime-status.md` — kompletter Implementierungsstatus (Increment 1, 1.1, 1.2, 2) + verbleibende App-Verdrahtung.
- `docs/air/air-platform-split.md` — 4-Projekt-Zielarchitektur, Datei-/Namespace-Mapping, csproj-Inhalte, Stage 2/3-Code (`AiMessage`, AIR.SDK).
- `docs/phase-8.3-resource-manager-spec.md` — freizugebende Spezifikation für Ressourcenprofile, Kapazität, Budget, Last, Auswahlgrenzen und Pflicht-Tests.
- `scripts/migrate-air-stage1.ps1` — Migrations-Script Stage 1 (mit Sicherheitschecks).
- `phase-7.0-ai-runtime.md` (Repo-Wurzel `H:\AAIAGitHub\`) — die ursprüngliche, finale Spec (Runtime + MCP-Adapter, Akzeptanzkriterien).
- Quell-Code: `src/AAIA.Air.Contracts/`, `src/AAIA.Air/`, `src/AAIA.Air.Mcp/` sowie `src/AAIA.ModuleManager/Services/Ai/Integration/`.
- Tests: `src/AAIA.ModuleManager.Tests/Ai/`.

## Namensentscheidung (fix)

Die Runtime heißt **AIR = AAIA Intelligence Runtime**, eingebettet in die **AAIA Intelligence Platform**. Die Ziel-Namespaces `AAIA.Air.Contracts`, `AAIA.Air` und `AAIA.Air.Mcp` sind umgesetzt.
