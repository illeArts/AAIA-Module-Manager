# Phase 7.0 — AI Runtime: Implementierungsstatus

Stand: Increment 1 (Foundation + MCP-Adapter + `status.get`) **+ 1.1** (Host-Abstraktion,
Tasks) **+ 1.2** (Rollen, Blackboard, Workflows, Memory, Collaboration, Capabilities).

## Increment 1.2 — KI-Betriebssystem-Bausteine

Die Runtime ist jetzt unter `Services/Ai/Runtime/` in Subnamespaces organisiert: `Hosts`, `Tasks`, `Workflows`, `Collaboration`, `Memory`, `Roles`, `Providers`.

- **Rollen** (`Roles/AiRole.cs`): Architect, Developer, Reviewer, Researcher, Tester, Installer, Documenter, Administrator. An der Session (`AiSession.Roles`). Jede KI kann jede Rolle einnehmen — modellneutral.
- **Blackboard** (`Collaboration/AiBlackboard`): ersetzt „SharedProjectState". Klassischer Multi-Agenten-Begriff. Einträge mit Topic, Status, Owner, Notes, **Priority**, **Progress**. Tools `aaia.blackboard.write/read`.
- **Workflow-Engine** (`Workflows/`): `AiWorkflow`/`AiWorkflowEngine` getrennt von Tasks. Task = eine Aufgabe, Workflow = kompletter Ablauf (Validate → Build → Package als Vorlage). Tools `aaia.workflow.standardPipeline/run`.
- **Projekt-Memory** (`Memory/AiProjectMemory`): KEIN Chat-Memory — hält Designentscheidungen, Begründungen, Zustimmungen fest (warum, welche KI, wer hat zugestimmt, wann). Tools `aaia.memory.record/query`.
- **Collaboration Manager** (`Collaboration/AiCollaborationManager`): verteilt nach **Verantwortung/Rolle** + Verfügbarkeit. Schlägt Reviewer/Tester vor (schließt den Autor aus), zeigt „wer arbeitet woran" und „wer wartet".
- **IAiCapabilityProvider** (`Providers/`): Module deklarieren benötigte externe Fähigkeiten (Filesystem, Scanner, Router, Docker, Git). Ergänzt `IAiToolProvider`.
- **Erweiterte Capability Negotiation** (`AiClientCapabilities`): Kontextfenster, Reasoning, Streaming, Vision, Files, Terminal, MCP-Version. Die Runtime passt sich dem Client an.
- **Keine modellspezifische Logik**: das Vendor-aus-Name-Mapping wurde entfernt. Nirgends `if Claude / if GPT` — die Runtime kennt nur Client, Capabilities, Permissions, Roles.

**Offen — bewusst nicht hier gemacht:** Extraktion der Runtime in ein eigenes Paket `AAIA.AI.Runtime` (+ `AAIA.AI.Runtime.Mcp`). Die Runtime ist dafür bereits 100 % abhängigkeitsfrei vom Module Manager. Dieser Schritt ist ein reiner Datei-Move + Projektreferenz und sollte compile-geprüft in der IDE erfolgen (im Sandbox kein Compiler, keine Löschrechte).

Increment-1.2-Tests ergänzt: Blackboard-Konflikt, Workflow-Pipeline, Memory mit Autor, Reviewer-Vorschlag nach Rolle, Capability-Profil → Tags.



## Increment 1.1 — Entkopplung, Tasks, Collaboration

**Host-Abstraktion** (`Services/Ai/Runtime/Hosts/`): Die Runtime kennt jetzt **keine konkrete App** mehr, nur Interfaces — `IAiStatusHost`, `IAiProjectHost`, `IAiFileHost`, `IAiPatchHost`, `IAiValidationHost`, `IAiBuildHost`, `IAiPackageHost`, `IAiIdeHost`, `IAiTerminalHost`, `IAiMarketplaceHost`. `AiHostRegistry` löst sie zur Laufzeit auf. Dadurch ist die Runtime für Module Manager, AAIAS, BBK, DUKI, Website und Mobile wiederverwendbar. Die 10 Kern-Tools rufen jetzt diese Interfaces; ist ein Host nicht registriert, liefert das Tool `host_unavailable` und verändert nichts.

**Tasks** (`Services/Ai/Runtime/Tasks/`): `AiTask` + `AiTaskManager` als Ebene ÜBER den Tool-Calls. Eine KI erstellt eine Aufgabe, übernimmt sie ("Ich übernehme Aufgabe X") und lässt die Schritte sequenziell laufen — jeder Schritt durchläuft die volle Sicherheits-Kette. Tools: `aaia.task.create/claim/list/run`.

**Shared Project State** (`Services/Ai/Runtime/Collaboration/`): `AiSharedProjectState` koordiniert mehrere KIs. Claude setzt "Login = InProgress, Owner: Claude"; ChatGPT sieht sofort "nicht bearbeiten". Tools: `aaia.state.set/list`. Setzt eine fremde Session einen aktiven Bereich, kommt ein `conflict` zurück.

Komposition (`AiRuntimeComposition`) nimmt jetzt `params IAiHost[]` und registriert jeden Host unter allen Interfaces, die er implementiert.

**Increment-1.1-Tests** (`AiRuntimeExtensionsTests.cs`): host_unavailable ohne Host, Tool ruft registrierten Host, Task create/claim/run, Claim-Konflikt, Shared-State-Blockade zwischen Sessions, Host-Registry-Auflösung.

---

## Increment 1 (Basis)

## Was umgesetzt ist

**Runtime-Kern** (`src/AAIA.ModuleManager/Services/Ai/Runtime/`) — reines C#, SDK-frei:

- `AiRuntimeService` — Orchestrator. Jeder Adapter ruft `InvokeToolAsync(...)`. Kette: Session → Tool aktiv → Capability → Permission → Workspace-Lock → Ausführung → Audit + Event.
- `AiSessionManager` / `AiSession` / `AiClientIdentity` — mehrere Clients gleichzeitig, je eigene Session.
- `AiCapabilityManager` — Capability Negotiation (Client meldet Fähigkeiten).
- `AiPermissionEngine` — Permissions pro Client/Projekt; Default nur `Read`; Sign/Marketplace hart gesperrt.
- `AiWorkspaceLockService` — Project/Folder/File-Locks mit Timeout, verhindert Schreibkollisionen.
- `AiRuntimeEventBus` — Push-Events (subscribe.pipeline), trägt Client-Identität.
- `AiAuditService` — jede Aktion mit Identität, Secret-Maskierung.
- `AiToolRegistry` + `AiToolVersioning` — Registry liegt in der Runtime (NICHT in MCP); SemVer-Auflösung, Black-Tools abgelehnt.
- `IAiToolProvider` — Module bringen eigene Tools mit (FRITZ!Box, WordPress, …); Registry lädt sie.
- `AaiaCoreToolsBootstrap` — registriert die 11 Kern-Tools + 4 Runtime-Tools.
- `AiRuntimeComposition` — Factory, die den ganzen Stack zusammensetzt.

**MCP-Adapter** (`src/AAIA.ModuleManager/Services/Ai/Mcp/`):

- `AaiaMcpBridgeOptions` — Port 39158, StreamableHttp, default deaktiviert, UI-Toggles. In `AppConfig.McpBridge`.
- `AaiaMcpAuthHandler` — zufälliges Bridge-Token, Bearer-Prüfung (konstantzeitlich), rotierbar.
- `AaiaMcpConfigService` — erzeugt Claude- und Codex-Config inkl. Token.
- `AaiaMcpAdapter` — SDK-frei: übersetzt Registry ↔ MCP, löst Sessions je Client auf.
- `AaiaMcpServer` — hostet das offizielle C# MCP SDK (ASP.NET Core, Streamable HTTP), nur `127.0.0.1`, Token-Middleware, `MapMcp`.

**Voll funktionsfähige Tools:** `aaia.status.get`, `aaia.session.whoami`, `aaia.events.subscribe`, `aaia.locks.acquire`, `aaia.locks.release`.
Die übrigen Kern-Tools sind korrekt registriert (Risk/Permission/Capability), ihre Handler liefern bis Increment 2 `not_implemented_yet` und verändern nichts.

**Tests** (`src/AAIA.ModuleManager.Tests/Ai/`): 18 Foundation-Tests (Registry, Black-Block, Disabled, status.get, Capability-Filter, Permission-Deny, Lock-Konflikt + Timeout, Session-Isolation, Versionierung, Audit, Sign-Block, Token, Rotation, Config). SDK-frei, laufen ohne MCP-Restore.

## Build & Test (lokal, Windows)

```
cd aaia-module-manager
dotnet restore
dotnet test src/AAIA.ModuleManager.Tests/AAIA.ModuleManager.Tests.csproj
```

Hinweis: `dotnet restore` zieht `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (in `AAIA.ModuleManager.csproj`, Version 1.4.0 — beim ersten Restore bestätigen/anpassen).

## Eine Nahtstelle zum Verifizieren

`AaiaMcpServer.StartAsync` verdrahtet die SDK-Handler (`ListToolsHandler`/`CallToolHandler`, Typen `Tool`, `ListToolsResult`, `CallToolResult`, `TextContentBlock`). Diese Symbolnamen können je SDK-Version minimal abweichen — beim ersten Build gegen die installierte SDK-Version abgleichen. Die Logik (Registry → Tool-Liste, Call → `InvokeToolAsync`) bleibt unverändert. Der Rest des Codes ist SDK-frei und unabhängig davon korrekt.

## Increment 2 — konkrete Module-Manager-Hosts (umgesetzt)

`Services/Ai/Integration/`:

- **`ModuleManagerHosts`** implementiert IAiStatus/Project/File/Patch/Validation/Build/Package/Ide/Terminal gegen die echten Phase-6-Services — keine neue Geschäftslogik:
  - Validate → `ProjectValidationOrchestrator.ValidateAsync`
  - Build → `BuildRunnerService.RestoreAndBuildAsync` (csproj wird aufgelöst), Issues strukturiert (code/title/file/line/explanation)
  - Package → `ExtensionPackageService.CreatePackageAsync`
  - Files → eigene Allowlist-Enumeration mit Excluded-Dirs (bin/obj/.git/.vs/packages), Block von `.env/.pem/.pfx/.key`, `key-info.json`, `secrets.json`; Read mit Path-Traversal-Schutz + 256-KB-Limit
  - Ide → `IdeDetectionService.Detect/OpenInIde`
  - Terminal → strikte Allowlist (`dotnet --info/restore/build/test`, `git status/diff/log --oneline`) via `ProcessRunner.RunCapturedAsync`, harte Token-Blockliste, Output-Limit
  - Status / Patch / Create → über `IModuleManagerAiBridge` (App-/UI-Zustand)
- **`IModuleManagerAiBridge`** — App-Schnittstelle: Status, Projektauflösung (csproj/Typ/Name), Patch-Approval+Apply (über vorhandenen Workflow), Projekt-Erstellung (Wizard). Bewusst dünn, damit die AIR die UI-Logik nicht reimplementiert.
- **`AiRuntimeConnectorPanel`** — framework-neutrales UI-Glue: baut die AIR mit `ModuleManagerHosts`, bietet Start/Stop, Token-Rotation, Claude-/Codex-Config, Listen für Sessions/Locks/Tools/Audit.

**Mehrclient-Tests** (`MultiClientTests.cs`): Claude + Codex gleichzeitig, parallele `status.get`, getrennte Per-Client-Permissions (Claude baut, Codex nur Read), Blackboard-Koordination zwischen beiden.

### Verbleibende App-Verdrahtung (in der IDE, compile-geprüft)

1. `ConnectorTabViewModel` implementiert `IModuleManagerAiBridge`:
   - `GetStatus()` aus realem Connector-/AAIAS-Status + geladenem Projekt,
   - `ResolveProject()` aus dem aktuell geladenen Projekt (csproj/Typ/Name),
   - `ApproveAndApplyPatchAsync()` über `AiConnectorServer.PatchProposalReceived` / `PatchApprovalViewModel` (Diff + Apply),
   - `CreateProjectAsync()` über den Wizard/`ProjectScaffoldingService`.
2. Connector-Tab-XAML: Sektion „AAIA AIR / MCP" an `AiRuntimeConnectorPanel` binden (Start/Stop, Token rotieren, Config kopieren, Sessions/Locks/Audit-Listen).
3. `AppConfig.McpBridge` in den Settings persistieren (ist bereits angelegt).

### Danach: AIR-Rename + Paket-Extraktion

Erst wenn alles lokal baut und Tests grün sind: `AAIA.ModuleManager.Services.Ai.Runtime` → `AAIA.Air` umbenennen und in eigenes Projekt `AAIA.Air` (+ `AAIA.Air.Mcp`) extrahieren. Phase 8 (AI Collaboration & Orchestration) danach.
