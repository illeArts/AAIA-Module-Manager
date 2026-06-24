# AIR — Übergabebericht (Handoff für die nächste Session)

**Stand:** Phase 7.0, Plattform-Split sowie Phase 8.1 bis 8.4 sind abgeschlossen. Phase 9.1 State Store ist abgeschlossen: Contracts, In-Memory-Store, kanonischer Codec und lokaler Datei-Store mit OS-Lock/Atomic-Replace/Crash-Tail-Recovery sind implementiert. Tests: 240/240 grün. Persistenz ist weiterhin deaktiviert und nicht mit der Runtime verdrahtet. Nächster Schritt: Phase 9.2 Durable Tasks/Execution Queue mit Snapshot-DTOs und `RecoveryRequired` beginnen.

## Wo wir stehen

Implementiert und gebaut:

- **Contracts** (`src/AAIA.Air.Contracts/`): BCL-only, keine Projekt- oder NuGet-Abhängigkeiten. Enthält öffentliche Typen, Enums, Sessions, Host-Interfaces, Task-/Workflow-Modelle, Events, Capability-Interfaces und `AiMessage`.
- **AIR-Runtime-Kern** (`src/AAIA.Air/`): `AiRuntimeService` mit Sicherheits-Kette, Messaging, Execution Queue/Scheduler, Resource Manager, Idempotenz, Tasks, Workflows, Blackboard und Memory. Referenziert nur Contracts.
- **MCP-Adapter** (`src/AAIA.Air.Mcp/`): lokaler token-geschützter Streamable-HTTP-Server sowie abgesicherte Phase-8-Werkzeuge mit geschlossenen Defaults, Ownership und Redaction.
- **SDK** (`src/AAIA.Air.SDK/`): öffentliche ETW-Entwickleroberfläche mit `AirToolProviderBase` und `AirToolBuilder`; referenziert ausschließlich Contracts.
- **Module-Manager-Integration** (`src/AAIA.ModuleManager/Services/Ai/Integration/`): app-spezifische Hosts, Bridge, Runtime-Beobachtung und bestätigte/auditierte lokale Admin-Aktionen. Die Runtime kennt den Module Manager nicht.

## ✅ Schritt 2 — ERLEDIGT (App-Verdrahtung)

`ConnectorTabViewModel` implementiert jetzt `IModuleManagerAiBridge` (Build grün):

- `GetStatus()` — aus `_server.IsRunning`, `_airPanel?.IsRunning`, `_currentContext`, `_aaias?.IsConnected`.
- `ResolveProject()` — Pfadprüfung gegen `_projectRoot`, Mapping `AiHandoffContext.ProjectType`-String → `NewProjectType`, `.csproj`-Suche.
- `ApproveAndApplyPatchAsync()` — `AiPatchProposalInput` → `AiPatchRequest`, TCS + `ShowPatchApproval`-Callback, wartet auf `OnPatchDecision`. AIR-Proposals (Präfix `air-`) werden von HTTP-Connector-Proposals in `OnPatchDecision` getrennt.
- `CreateProjectAsync()` — mappt auf `ScaffoldOptions` → `ProjectScaffoldingService.ScaffoldAsync`.

`AiRuntimeConnectorPanel` im Konstruktor instanziiert (`new AiRuntimeConnectorPanel(config.McpBridge, this)`). `MainWindowViewModel` übergibt jetzt `TesterTab.AaiasConn`. XAML-Sektion „AAIA AIR / MCP" als aufklappbarer `Expander` mit Start/Stop/Token rotieren/Config kopieren und den 4 Statuslisten.

## NÄCHSTER SCHRITT (genau hier weitermachen)

1. Persistierbare Task-/Execution-Snapshot-DTOs ohne Sessions, Locks oder Handler definieren.
2. `RecoveryRequired` für vor dem Crash laufende Tasks/Executions einführen.
3. Deterministischen Snapshot-Export/Import zunächst gegen den In-Memory-Store testen.
4. Keine MCP-Erweiterung oder automatische Wiederholung laufender Tools einführen.

## Harte Regeln (nicht verletzen)

- **Keine neuen AIR-Funktionen mehr in Phase 7.0.** Nur Build-/Verdrahtungsfehler lösen.
- **AIR ist herstellerneutral:** nirgends `if Claude` / `if GPT`. Nur Client / Capabilities / Permissions / Roles. (Vendor wird NICHT aus dem Namen abgeleitet.)
- **Phase 8 ist geschlossen:** Änderungen an Messaging, Scheduler, Resource Manager oder MCP/UI benötigen einen neuen expliziten Scope; Phase 9 erweitert ausschließlich Durability/Recovery.
- **AIR kennt keine App.** Apps (Module Manager, AAIAS, BBK, DUKI, Website, Mobile) sind Nutzer über Hosts/Contracts — nie umgekehrt.
- **Sicherheit unverändert lassen:** Bridge-Token-Pflicht, nur 127.0.0.1, Default deaktiviert, Patch nur über Approval, Terminal nur Allowlist, keine Secrets, Path-Traversal-Schutz, Black-Tools existieren nicht.

## Wo alle Informationen liegen

- `docs/phase-7.0-ai-runtime-status.md` — kompletter Implementierungsstatus (Increment 1, 1.1, 1.2, 2) + verbleibende App-Verdrahtung.
- `docs/air/air-platform-split.md` — 4-Projekt-Zielarchitektur, Datei-/Namespace-Mapping, csproj-Inhalte, Stage 2/3-Code (`AiMessage`, AIR.SDK).
- `docs/phase-8.3-resource-manager-spec.md` — implementierte Spezifikation für Ressourcenprofile, Kapazität, Budget, Last, Auswahlgrenzen und Pflicht-Tests.
- `docs/phase-8.4-adapter-mcp-ui-spec.md` — Spezifikation für Permissions, MCP-Werkzeuge, Adaptergrenzen, UI-Freigaben und 30 Pflicht-Tests.
- `docs/phase-9-durable-runtime-state-spec.md` — Spezifikation für lokalen State Store, Journal, geschützte Payloads, Crash-Recovery und 40 Pflicht-Tests.
- `scripts/migrate-air-stage1.ps1` — Migrations-Script Stage 1 (mit Sicherheitschecks).
- `phase-7.0-ai-runtime.md` (Repo-Wurzel `H:\AAIAGitHub\`) — die ursprüngliche, finale Spec (Runtime + MCP-Adapter, Akzeptanzkriterien).
- Quell-Code: `src/AAIA.Air.Contracts/`, `src/AAIA.Air/`, `src/AAIA.Air.Mcp/` sowie `src/AAIA.ModuleManager/Services/Ai/Integration/`.
- Tests: `src/AAIA.ModuleManager.Tests/Ai/`.

## Namensentscheidung (fix)

Die Runtime heißt **AIR = AAIA Intelligence Runtime**, eingebettet in die **AAIA Intelligence Platform**. Die Ziel-Namespaces `AAIA.Air.Contracts`, `AAIA.Air` und `AAIA.Air.Mcp` sind umgesetzt.
