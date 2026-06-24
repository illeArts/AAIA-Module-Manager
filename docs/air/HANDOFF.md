# AIR — Übergabebericht (Handoff für die nächste Session)

**Stand:** Phase 7.0, Plattform-Split, Phase 8.1 bis 8.4 und Phase 9 sind abgeschlossen. Phase 10.1 besitzt jetzt BCL-only Delta-Contracts, eine geschlossene Event-Registry, prüfsummenvalidierten Codec, deterministischen Reducer und ein threadsicheres In-Memory-Referenzjournal. Phase-9-Snapshots bleiben lesbar; der produktive Writer schreibt weiterhin vollständige Checkpoints. Tests: 295/295 grün. Aktivierung ist opt-in über `AirPersistence.Enabled`; Standard bleibt AUS. Nächster Schritt: zentrale durable Mutationstransaktion und Snapshot-Bündelung implementieren, dann den produktiven Schreibpfad erst nach grüner Migrations-/Crash-Matrix umschalten.

**Dokumentationsregel ab jetzt:** Jede technische Phase aktualisiert die betroffenen Benutzer-,
Entwickler-, Admin-, Architektur- und Website-Hilfebereiche und erhält einen Abschlussnachweis
nach `docs/phases/PHASE_FINAL_TEMPLATE.md`. Phase 11.5 baut daraus das versionierbare
Dokumentationssystem; die Foundation wird früh vorgezogen, ohne den Phase-10-Runtime-Scope
oder dessen nächsten Implementierungsschritt zu verändern. Historische Begriffe und aktueller
Implementierungsstatus bleiben nach `docs/DOCUMENTATION_TRUTH_RULE.md` strikt getrennt.

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

1. Zentrale durable Mutationstransaktion auf dem bestehenden Single Writer implementieren: Sequenz zuweisen, Delta flushen, dann In-Memory anwenden und bestätigen.
2. Snapshot-Trigger und Compact zunächst gegen In-Memory-/Fake-Store einschließlich Crash-Injection testen.
3. Phase-9-Checkpoint lesbar halten; noch keine neuen Events produktiv schreiben, bevor Migrations- und Crash-Matrix grün sind.
4. Keine neuen MCP-Tools, Permissions oder Orchestrierungsfunktionen einführen.

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
- `docs/phase-10-production-hardening-spec.md` — Spezifikation für Delta-Journal, native Protectoren, portablen Lifecycle und Betriebsnachweis.
- `docs/phase-11.5-documentation-release-readiness-spec.md` — Dokumentationssystem, feste Phasenabschlussregel und spätere Website-/PDF-/In-App-Ausgaben.
- `docs/README.md` — kanonischer Einstieg in Benutzer-, Entwickler-, Admin-, Architektur-, Troubleshooting-, Glossar- und Website-Dokumentation.
- `scripts/migrate-air-stage1.ps1` — Migrations-Script Stage 1 (mit Sicherheitschecks).
- `phase-7.0-ai-runtime.md` (Repo-Wurzel `H:\AAIAGitHub\`) — die ursprüngliche, finale Spec (Runtime + MCP-Adapter, Akzeptanzkriterien).
- Quell-Code: `src/AAIA.Air.Contracts/`, `src/AAIA.Air/`, `src/AAIA.Air.Mcp/` sowie `src/AAIA.ModuleManager/Services/Ai/Integration/`.
- Tests: `src/AAIA.ModuleManager.Tests/Ai/`.

## Namensentscheidung (fix)

Die Runtime heißt **AIR = AAIA Intelligence Runtime**, eingebettet in die **AAIA Intelligence Platform**. Die Ziel-Namespaces `AAIA.Air.Contracts`, `AAIA.Air` und `AAIA.Air.Mcp` sind umgesetzt.
