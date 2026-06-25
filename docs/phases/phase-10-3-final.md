# Phase 10.3 — Abschlussdokumentation

> Status: abgeschlossen
> Geprüfter Stand: 2026-06-25
> Verantwortlicher Scope: `AAIA.Air.Persistence` und Module-Manager-AIR-Host

## 1. Was wurde gebaut?

Ein app-neutraler AIR-Runtime-Lifecycle mit:

- `InitializeAsync`
- `CompleteRecoveryAsync`
- `GetDiagnosticsAsync`
- `CreateReadinessLease`
- `StopAsync`
- idempotentem `DisposeAsync`

Der Module Manager startet und stoppt die AIR/MCP-Bridge jetzt über diesen Lifecycle.

## 2. Warum wurde es gebaut?

Vor 10.3 war Start/Stop direkt im Module-Manager-Panel verdrahtet. Dadurch war der korrekte
Zeitpunkt für Adapter-Start, Recovery-Freigabe, Shutdown-Snapshot und Writer-Freigabe nicht als
wiederverwendbare Host-Grenze verfügbar.

## 3. Welche Dateien wurden geändert?

- `src/AAIA.Air/Persistence/AiRuntimeLifecycle.cs`
- `src/AAIA.Air/Persistence/AiRuntimePersistenceCoordinator.cs`
- `src/AAIA.Air/AiRuntimeService.cs`
- `src/AAIA.Air.Contracts/AiRuntimeStateContracts.cs`
- `src/AAIA.ModuleManager/Services/Ai/Integration/AiRuntimeConnectorPanel.cs`
- `src/AAIA.ModuleManager.Tests/Ai/Runtime/Phase10LifecycleTests.cs`
- Phase-10-Spezifikation, AIR-Handoff und diese Abschlussdatei

## 4. Welche Architekturentscheidungen wurden getroffen?

- Der Lifecycle kapselt genau einen Persistenzkoordinator und optional Adapter-Start/Stop.
- Adapter-Start erfolgt erst nach erfolgreichem Recovery/Ready.
- `RecoveryRequired` startet keinen Adapter.
- Readiness-Leases werden bei Stop/Recovery-Statuswechsel invalidiert.
- Durable Mutationen prüfen das Runtime-Readiness-Gate unmittelbar vor der Tool-Ausführung.
- `StopAsync` setzt den Lifecycle auf `Stopping`, erzeugt beim Typed-Delta-Writer einen
  Shutdown-Snapshot, flusht/freigibt den Writer und stoppt danach den Adapter.

## 5. Welche Sicherheitsregeln gelten?

- Keine mutierenden Adapteraufrufe nach Stop.
- Keine zweite Writer-Öffnung außerhalb des Lifecycle.
- Diagnose bleibt read-only.
- Shutdown-Timeouts werden mit `state_shutdown_incomplete` sichtbar.
- Ungültige Readiness wird mit `state_readiness_expired` sichtbar.

## 6. Welche Tests müssen grün sein?

- Adapter startet erst, wenn Persistenz `Ready` ist.
- Readiness-Lease wird nach Stop ungültig.
- Durable Tool-Aufruf nach Lifecycle-Stop wird abgelehnt.
- Vollständige Regression: 319/319.

## 7. Was darf nicht verletzt werden?

- Keine neuen MCP-Tools oder Permissions.
- Keine alternative Mutationswarteschlange im Adapter.
- Kein Start der mutierenden Bridge vor Recovery-Abschluss.
- Kein erfolgreicher Shutdown-Bericht ohne Store-Flush/Writer-Freigabe.

## 8. Bekannte Grenzen / offene Punkte

- 10.4 muss die Conformance-, Last-, Crash- und Soak-Nachweise liefern.
- Backpressure-Kanal und Betriebsmetriken sind noch nicht vollständig umgesetzt.

## 9. Nächster Schritt

Phase 10.4 — Conformance-, Crash-, Last-/Soak- und Betriebsnachweis.

## 10. Relevanz für Benutzerhandbuch

Keine Bedienänderung. Start/Stop der AIR/MCP-Bridge bleibt identisch, ist intern aber stärker
abgesichert.

## 11. Relevanz für Entwicklerdokumentation

Hosts sollen `AiRuntimeLifecycle` verwenden und nicht direkt Adapter plus
`AiRuntimePersistenceCoordinator` parallel starten.

## 12. Relevanz für Administratorhandbuch

Shutdown-Fehler werden mit `state_shutdown_incomplete` diagnostizierbar. RecoveryRequired bleibt
ein lokaler Wartungszustand vor Adapterfreigabe.

## 13. Relevanz für Webseite / öffentliche Hilfe

Keine direkte öffentliche Funktionsänderung.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

10.3 ist implementiert. Der nächste Scope ist 10.4. Keine neuen Orchestrierungsfunktionen
einführen; es geht um Nachweis, Crash-/Soak-Matrix, Backpressure und Betriebsmetriken.
