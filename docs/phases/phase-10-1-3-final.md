# Phase 10.1.3 — Abschlussdokumentation

> Status: abgeschlossen
> Geprüfter Stand: 2026-06-25
> Verantwortlicher Scope: `AAIA.Air.Persistence`

## 1. Was wurde gebaut?

Der produktive AIR-Persistenzkoordinator kann jetzt kontrolliert vom Phase-9-Checkpoint-Writer
auf den typisierten Phase-10-Delta-Writer umgeschaltet werden. Der alte Checkpoint-Pfad bleibt
lesbar und per Rollback-Schalter weiterhin schreibbar.

## 2. Warum wurde es gebaut?

Phase 9 schrieb pro bestätigter Mutation einen vollständigen Orchestrierungs-Checkpoint. Das
war korrekt, aber zu groß für produktive Mutationsraten. 10.1.3 schaltet den produktiven Writer
auf kleine typisierte Deltas um, ohne den bestehenden Recovery-Pfad abzuschneiden.

## 3. Welche Dateien wurden geändert?

- `src/AAIA.Air.Contracts/AiRuntimeStateContracts.cs`
- `src/AAIA.Air/Persistence/AiRuntimePersistenceCoordinator.cs`
- `src/AAIA.Air/Persistence/AiDurableMutationTransactionCoordinator.cs`
- `src/AAIA.ModuleManager.Tests/Ai/Runtime/Phase9ActivationTests.cs`
- `README.md`
- Phase-10-Spezifikation, AIR-Handoff und diese Abschlussdatei

## 4. Welche Architekturentscheidungen wurden getroffen?

- Aktivierung läuft über `AiRuntimePersistenceOptions.UseTypedDeltaWriter`.
- Rollback läuft über `AiRuntimePersistenceOptions.RollbackToPhase9CheckpointWriter`.
- Vor dem ersten Typed-Delta-Writer-Start kann der Host ein Backup über
  `IAiRuntimeStateMaintenanceStore` erzeugen; das geschieht vor dem Öffnen der Writer-Session.
- Der produktive Writer liest Phase-9-Snapshots und Phase-10-Deltas gemischt.
- Beim ersten Typed-Delta-Start wird ein verifizierter Snapshot/Manifest mit
  `typedDeltaJournal` erzeugt.
- Runtime-Mutationen werden aus dem aktuellen durable Runtime-Snapshot gegen den zuletzt
  bestätigten Snapshot in typisierte Eventfamilien übersetzt.

## 5. Welche Sicherheitsregeln gelten?

- Kein `orchestration.checkpoint` wird geschrieben, wenn der Typed-Delta-Writer aktiv ist.
- Rollback erzwingt den alten Writer vollständig und erzeugt wieder Phase-9-Checkpoints.
- Operation-IDs werden pro produktivem Mutationsbatch eindeutig erzeugt.
- Externe Tool-Seiteneffekte bleiben über die vorhandene Idempotenzstruktur abgesichert; die
  persistierten Idempotenzdatensätze werden als `idempotency.stored`/`idempotency.evicted`
  replaybar.

## 6. Welche Tests müssen grün sein?

- Typed Writer schreibt typisierte Deltas statt Phase-9-Checkpoint.
- Phase-9-Snapshot migriert und bleibt zusammen mit neuen Deltas recoverbar.
- Rollback-Schalter erzwingt Phase-9-Checkpoint-Writer.
- Vollständige Regression: 313/313.

## 7. Was darf nicht verletzt werden?

- Phase-9-Checkpoint-Lesbarkeit bleibt erhalten.
- Kein Compact vor Snapshot-Verify und Manifest-Flush.
- Keine doppelte Wirkung derselben Operation-ID.
- Keine Einführung neuer MCP-Tools oder Permissions.
- Keine macOS-/Linux-Protector-Implementierung in 10.1.3; das ist Phase 10.2.

## 8. Bekannte Grenzen / offene Punkte

- Native macOS-/Linux-Protectoren fehlen weiterhin und folgen erst in Phase 10.2.
- Die produktive Aktivierung bleibt opt-in.
- Last-/Soak-Nachweise gehören weiterhin zum späteren 10.4-Betriebsnachweis.

## 9. Nächster Schritt

Phase 10.2 — native macOS-/Linux-Protectoren mit fail-closed Verhalten, Key-ID und späterer
Rotation.

## 10. Relevanz für Benutzerhandbuch

Keine Bedienänderung. Persistenz bleibt opt-in; Rollback ist eine Betreiber-/Host-Option.

## 11. Relevanz für Entwicklerdokumentation

`UseTypedDeltaWriter` und `RollbackToPhase9CheckpointWriter` sind die relevanten Schalter für
Host-Integrationen. Neue Writer dürfen nicht parallel zur zentralen AIR-Persistenz geöffnet
werden.

## 12. Relevanz für Administratorhandbuch

Aktivierung und Rollback müssen als Betriebsablauf dokumentiert werden: Backup vor Migration,
Start prüfen, Manifest-Flag `typedDeltaJournal` prüfen, bei Bedarf Rollback-Schalter setzen.

## 13. Relevanz für Webseite / öffentliche Hilfe

README ergänzt Linux-Installation und Linux-Selbstbau inklusive `installer/build-linux.sh`.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

10.1.3 ist implementiert. Der nächste technische Scope ist 10.2; keine weiteren
Writer-Migrationsarbeiten vorziehen, bevor native Protectoren sauber spezifiziert und getestet
sind.
