# Phase 10 — AIR Production Hardening & Portable Hosting: Spezifikation

> Status: abgeschlossen; 10.4 Betriebsnachweis implementiert
> Scope: effiziente Durability, native Plattform-Sicherheit, app-neutraler Lifecycle und Betriebsnachweis

## 1. Ausgangslage und Ziel

Phase 9 stellt einen crash-sicheren, opt-in aktivierbaren AIR-Zustand bereit. Der aktuelle
Aktivierungspfad ist absichtlich konservativ: Jede relevante Mutation schreibt einen
vollständigen Orchestrierungs-Checkpoint. Das ist korrekt, skaliert aber bei großen Tasks,
Audit-Historien und hoher Mutationsrate nicht. Zudem existiert ein produktiver Protector
nur für Windows; macOS und Linux bleiben korrekt fail-closed.

Phase 10 macht den vorhandenen Funktionsumfang produktionsreif und portabel. Es kommen
keine neuen Orchestrierungsfähigkeiten hinzu. Stattdessen werden Mutationen als kleine,
typisierte Journal-Events dauerhaft bestätigt, Snapshots kontrolliert gebündelt,
plattformnative Protectoren ergänzt und der Runtime-Start aus der Module-Manager-UI in
einen wiederverwendbaren Host-Lifecycle verschoben.

## 2. Nicht-Ziele

Phase 10 implementiert ausdrücklich nicht:

- neue MCP-Tools, neue MCP-Permissions oder Remote-Administration,
- Cloud-Synchronisation, Mehrknoten-Konsens oder verteilte Locks,
- dauerhafte Message-Mailboxen, Blackboard- oder freie Memory-Payloads,
- neue Scheduler-, Resource-Manager- oder Workflow-Fachlogik,
- Vendor-/Modell-Sonderbehandlung,
- automatische Schlüsselwiederherstellung über Netzwerk oder Benutzerkonto,
- stillen Fallback auf Klartext, schwache lokale Schlüsseldateien oder Umgebungsvariablen,
- automatische Aktivierung bestehender Installationen,
- allgemeine Datenbank- oder Message-Broker-Abhängigkeiten.

## 3. Inkremente

| Inkrement | Inhalt | Status |
|---|---|---|
| 10.1 | Typisiertes Delta-Journal und gebündelte Snapshots | 10.1.3 umgesetzt |
| 10.2 | Native Protectoren, Rotation und Protector-Migration | native Protectoren umgesetzt; Rotation bleibt Folgeschritt |
| 10.3 | App-neutraler Runtime-Lifecycle und Readiness-Gates | umgesetzt |
| 10.4 | Conformance-, Last-, Soak- und Betriebsnachweis | umgesetzt |

Jedes Inkrement erhält einen eigenen PR. Die bestehenden vollständigen Checkpoints bleiben
während 10.1 als lesbarer Migrationspfad erhalten und werden erst nach grüner Replay- und
Crash-Matrix nicht mehr neu geschrieben.

### Implementierungsstand 10.1

Die Foundation umfasst BCL-only Contracts, eine geschlossene Registry aller Eventfamilien,
den prüfsummenvalidierten Journal-Codec und einen deterministischen Reducer. In 10.1.2 kam
die Store-basierte Referenztransaktion hinzu: vollständige Vorabvalidierung, Append und Flush
vor In-Memory-Apply, exakt-einmal Operation-IDs, Snapshot nach 1.000 Events oder 10 Minuten,
Verify und Manifest-Flush vor Compact sowie expliziter Shutdown-Snapshot. In 10.1.3 wurde der
produktive Single Writer optional auf typisierte Delta-Events umgestellt. Phase-9-Snapshots
und `orchestration.checkpoint` bleiben als Replay-Basis lesbar; der Rollback-Schalter erzwingt
weiterhin den alten vollständigen Checkpoint-Writer.

## 4. Architekturgrenzen

### 4.1 AIR-Kern

`AAIA.Air` verantwortet:

- Mutationsreihenfolge, Operation-ID und Event-Typ,
- atomare In-Memory-Anwendung nach dauerhaftem Journal-Flush,
- Replay, Snapshot-Trigger, Backpressure und Readiness-Zustand,
- redigierte Diagnose und host-neutrale Betriebsmetriken.

Der Kern kennt weiterhin keine Dateipfade, DPAPI, Keychain, Secret Service, Avalonia,
MCP-Transportdetails oder konkrete App-Rollen.

### 4.2 Host

Ein Host stellt bereit:

- `IAiRuntimeStateStore` und `IAiStateProtector`,
- lokale Owner/Admin-Autorisierung und Bestätigung,
- plattformnative Schlüsselablage,
- Darstellung von Status und Diagnose,
- Start/Stop des jeweiligen Adapters erst nach AIR-Readiness.

Der Host darf keine Journal-Events erfinden, Replay-Reihenfolgen verändern oder
Recovery-Zustände automatisch freigeben.

### 4.3 Adapter

MCP und spätere Adapter konsumieren ausschließlich den zentralen Lifecycle-Status. Ein
Adapter darf weder einen eigenen Recovery-Pfad noch eine alternative Mutationswarteschlange
implementieren. `Ready` ist die einzige Freigabe für mutierende Aufrufe.

## 5. Inkrement 10.1 — Typisiertes Delta-Journal

### 5.1 Mutations-Envelope

Jede durable Mutation verwendet ein `AiDurableMutationEnvelope` mit:

- `SchemaVersion`,
- monotoner `Sequence`,
- stabiler `OperationId`,
- `MutationType`,
- `OccurredAtUtc`,
- optionalem stabilen Actor-/Client-Fingerprint,
- geschützter oder redigierter typisierter Payload,
- Input-Fingerprint bei idempotenten Adapteroperationen.

Operation-ID und Mutation werden vor der In-Memory-Anwendung in einem einzigen Writer-
Critical-Section geprüft. Dieselbe Operation darf beim Replay exakt einmal wirken.

### 5.2 Pflicht-Eventtypen

Mindestens folgende Eventtypen sind versioniert zu definieren:

- `task.created`, `task.claimed`, `task.claim_released`, `task.step_changed`, `task.settled`,
- `execution.queued`, `execution.leased`, `execution.state_changed`, `execution.recovery_resolved`,
- `budget.created`,
- `reservation.created`, `reservation.committed`, `reservation.released`, `reservation.expired`,
- `idempotency.stored`, `idempotency.evicted`,
- `audit.recorded`,
- `runtime.recovery_checkpoint`.

Freie Eventnamen und unbekannte Payload-Polymorphie sind nicht zulässig. Jeder Eventtyp
besitzt einen BCL-only Contract und einen expliziten Reader.

### 5.3 Transaktionsregel

Für rein interne Mutationen gilt:

1. Eingabe validieren und Mutation vorbereiten.
2. Event mit neuer Sequenz erzeugen.
3. Event appendieren und dauerhaft flushen.
4. Mutation exakt einmal im Speicher anwenden.
5. Erfolg an Aufrufer zurückgeben.

Für bereits ausgeführte externe Seiteneffekte wird das Resultat mit stabiler Operation-ID
vor der Antwort journalisiert. Schlägt der Flush fehl, bleibt die Runtime fail-closed; ein
erneuter Request muss über Idempotenz dasselbe Resultat auflösen und darf den Seiteneffekt
nicht wiederholen.

### 5.4 Snapshot-Policy

- Snapshot nach 1.000 Journal-Events oder 10 Minuten, zuerst eintretende Grenze.
- Ein expliziter Shutdown darf zusätzlich einen Snapshot erzeugen.
- Snapshot-Erzeugung blockiert die Eventannahme nicht unbegrenzt; gleichzeitig bestätigte
  Events bleiben nach der Snapshot-Sequenz im Journal.
- Kompaktierung erfolgt erst nach Snapshot-Verify und Manifest-Flush.
- Ein normaler Einzel-Event darf keinen vollständigen Orchestrierungs-Snapshot enthalten.
- Payload- und Store-Quoten bleiben aus Phase 9 bestehen.

### 5.5 Migration vom Phase-9-Checkpoint

Der Reader akzeptiert weiterhin `orchestration.checkpoint`. Beim ersten erfolgreichen Start
wird daraus deterministisch ein aktueller Snapshot erzeugt. Neue Mutationen verwenden danach
nur Delta-Events. Das alte Original wird vor Migration gesichert und nicht in-place
überschrieben, bevor der neue Snapshot verifiziert ist.

## 6. Inkrement 10.2 — Native Protectoren

Implementierungsstand: Windows-DPAPI-v1 bleibt lesbar und ist weiter die Referenz für bestehende
Installationen. Für neue native Payloads existiert ein AES-GCM-Envelope mit nicht geheimer
Key-ID und AAD-Bindung an Store-ID, Record-Typ, Record-ID und Schema-Version. macOS verwendet
die lokale Keychain über den nativen `security`-Client. Linux verwendet Secret Service über
`secret-tool` und startet ohne aktive DBus-Benutzersitzung fail-closed mit
`state_protector_unavailable`. Fehlt ein vorhandener Key, schlägt die Entschlüsselung mit
`state_protector_key_missing` fehl. Schlüsselrotation und Offline-Protector-Migration bleiben
als separater Wartungspfad offen.

### 6.1 Plattformen

- Windows: DPAPI CurrentUser bleibt Referenzimplementierung.
- macOS: Keychain Services mit benutzergebundenem, nicht exportiertem Schlüssel.
- Linux Desktop: Secret Service über die aktive Benutzersitzung.
- Linux ohne verfügbaren Secret Service: `state_protector_unavailable`, kein Start der
  schreibenden Runtime und kein Schlüsseldatei-Fallback.

Alle Implementierungen binden Ciphertext kryptographisch an Store-ID, Record-Typ,
Record-ID und Schema-Version. UI-Prompts während Hintergrund-Recovery sind verboten.

### 6.2 Schlüsselkennung und Rotation

Geschützte Payloads tragen eine nicht geheime `ProtectorKeyId`. Rotation läuft nur als
lokale Owner/Admin-Wartungsaktion:

1. unveränderliches Backup,
2. neuen nativen Schlüssel erzeugen,
3. alle Payloads offline entschlüsseln und mit neuer Key-ID versiegeln,
4. vollständige Verify-Runde,
5. atomarer Store-Wechsel,
6. Audit und erst danach optionales Entfernen der alten Key-Referenz.

Ein Abbruch muss den alten Store und Schlüssel vollständig nutzbar lassen. Ein fehlender
alter Schlüssel führt zu read-only Diagnose, niemals zu leerem Zustand.

### 6.3 Datenminimierung

Protectoren erhalten nur die zu schützende Payload und den Schutzkontext. Sie dürfen keine
Task-Titel, Projektpfade, Actor-Namen oder Tool-Ergebnisse als Schlüsselmetadaten in native
Keychains schreiben.

## 7. Inkrement 10.3 — Portabler Runtime-Lifecycle

Implementierungsstand: `AiRuntimeLifecycle` kapselt Start, Recovery, Readiness-Lease,
Diagnose, Stop und Dispose. Module Manager verwendet den Lifecycle für die AIR/MCP-Bridge,
statt den Server direkt vor dem Persistenzstatus zu starten. `AiRuntimeService` besitzt ein
Readiness-Gate, das durable Mutationen nach Stop oder bei ungültiger Lease mit
`state_readiness_expired` blockiert. `AiRuntimePersistenceCoordinator.StopAsync` erzeugt beim
Typed-Delta-Writer einen Shutdown-Snapshot, flusht den Store und gibt den Writer frei.

### 7.1 Zustandsmaschine

Der gemeinsame Lifecycle besitzt genau diese Zustände:

`Disabled → OpeningStore → Recovering → RecoveryRequired | Ready | RecoveryFailed → Stopping → Stopped`

`Quarantined` ist ein spezialisierter `RecoveryFailed`-Betriebszustand. Übergänge sind
monoton pro Startversuch und werden als redigierte Events veröffentlicht.

### 7.2 Host-API

Ein app-neutraler Bootstrap stellt mindestens bereit:

- `InitializeAsync`,
- `CompleteRecoveryAsync`,
- `GetDiagnosticsAsync`,
- `CreateReadinessLease` für Adapter,
- `StopAsync` mit Flush-/Snapshot-Grenze,
- `DisposeAsync` als idempotenten Fallback.

Module Manager, AAIAS oder weitere Apps verwenden dieselbe API. Keine App darf direkt
einen Writer öffnen und parallel einen zweiten Lifecycle betreiben.

### 7.3 Readiness-Lease

Adapter erhalten eine kurzlebige, runtimegebundene Readiness-Lease. Sie wird bei
`Recovering`, `RecoveryRequired`, `RecoveryFailed` oder `Stopping` ungültig. Mutierende
Aufrufe prüfen die Lease unmittelbar vor der Fachmutation. Read-only Diagnose benötigt
keine Lease, darf aber keine Sessions, Secrets oder Payloads ausgeben.

### 7.4 Geordneter Shutdown

1. neue mutierende Aufrufe sperren,
2. laufende interne Mutationen bis zu einer konfigurierten Frist abschließen,
3. Journal flushen,
4. optionalen Snapshot/Compact ausführen,
5. Adapter stoppen,
6. Writer freigeben.

Timeout oder Flush-Fehler wird sichtbar protokolliert; ein Prozessende darf niemals als
erfolgreicher sauberer Shutdown ausgegeben werden, wenn der Flush nicht bestätigt ist.

## 8. Inkrement 10.4 — Betriebsnachweis

Implementierungsstand: Der Betriebsnachweis ergänzt gezielte Conformance-Tests für
Backpressure bei blockiertem Writer, read-only Diagnose während Writer-Last,
Shutdown-Timeouts mit `state_shutdown_incomplete` und wiederholte Crash/Restart-Zyklen ohne
Sequenzlücken. Die vollständige Regression dient als aktueller Release-Nachweis; lange
24-Stunden-Soak-Läufe bleiben ein optionales Benchmark-Profil außerhalb der normalen Unit-Suite.

### 8.1 Conformance-Kit

Jeder Store-/Protector-/Host-Adapter muss dieselbe öffentliche Testsuite bestehen:

- Single-Writer und parallele read-only Diagnose,
- Schema, Checksum, Quota und unbekannte Events,
- Crash an jeder Append-/Flush-/Snapshot-/Replace-Grenze,
- falscher Benutzer, falscher Kontext und fehlender Schlüssel,
- Rotation mit Abbruch an jeder Phase,
- Lifecycle- und Readiness-Übergänge,
- Redaction und keine persistierten Sessions/Secrets.

### 8.2 Last und Soak

Referenzszenarien:

- 10.000 Idempotenzdatensätze,
- 50.000 Audit-Einträge,
- 10.000 Tasks und 10.000 Executions innerhalb der definierten Quoten,
- parallele Mutationen aus mindestens 16 Clients,
- 24-Stunden-Soak mit periodischen Snapshots und Kompaktierungen,
- wiederholter Crash/Restart während hoher Journalrate.

Der Nachweis prüft Sequenzlücken, doppelte Operationen, Speicherwachstum, Lock-Leaks und
unbegrenzte Warteschlangen. Hardwareabhängige Zeitbudgets werden im Benchmark-Profil
dokumentiert; Korrektheit darf für Durchsatz nie abgeschwächt werden.

### 8.3 Betriebsmetriken

Erlaubte Metriken ohne Payload:

- Lifecycle-Zustand und Dauer je Übergang,
- Journal-Sequenz, Eventrate, Flush-Latenz und Backpressure-Dauer,
- Snapshot-Sequenz, Dauer, Größe und letzter Verify-Status,
- Recovery-/Replay-Zähler,
- Anzahl `RecoveryRequired`, Quarantänen und Wartungsfehler.

Client-Namen, Projektpfade, Task-Titel, Tool-Inputs und Resultate sind keine Metriklabels.

## 9. Backpressure und Fehlerverhalten

- Genau ein begrenzter Writer-Kanal; keine unbegrenzte Task-Sammlung.
- Bei blockiertem Writer werden neue durable Mutationen nach `WriterBackpressureTimeout` mit
  `state_backpressure` abgewiesen.
- Read-only Diagnose bleibt verfügbar.
- Flush-, Protector- oder Quota-Fehler setzt den Lifecycle auf `RecoveryFailed`.
- Nach `RecoveryFailed` wird keine weitere Mutation angenommen.
- Ein Retry des Lifecycle ist erst nach explizitem Stop/Neuinitialisieren erlaubt.
- Fehlertexte werden redigiert; stabile Reason-Codes bleiben erhalten.

Neue Reason-Codes:

- `state_backpressure`
- `state_shutdown_incomplete`
- `state_protector_key_missing`
- `state_protector_rotation_failed`
- `state_lifecycle_invalid_transition`
- `state_readiness_expired`

## 10. Pflicht-Tests

### Delta-Journal und Replay

1. Jede Pflichtmutation besitzt genau einen registrierten Eventtyp.
2. Ein normaler Delta-Event enthält keinen vollständigen Snapshot.
3. bestätigte Mutation ist nach sofortigem Crash vorhanden.
4. ungeflushte vorbereitete Mutation wird nicht als bestätigt gemeldet.
5. gleiche Operation-ID wirkt beim Replay exakt einmal.
6. andere Payload unter gleicher Operation-ID ist Konflikt.
7. Sequenzen bleiben bei 16 parallelen Schreibern eindeutig und lückenlos.
8. unbekannter Eventtyp stoppt vor `Ready`.
9. beschädigte Event-Payload stoppt vor In-Memory-Anwendung.
10. Phase-9-Checkpoint migriert deterministisch.
11. Migration ist bei erneutem Start idempotent.
12. Crash während Migration liefert alten oder neuen vollständigen Zustand.

### Snapshot, Compact und Backpressure

13. 999 Events erzeugen keinen Schwellen-Snapshot.
14. Event 1.000 erzeugt einen verifizierten Snapshot.
15. Zeitgrenze erzeugt Snapshot auch bei niedriger Eventrate.
16. parallele Events während Snapshot bleiben hinter dessen Sequenz erhalten.
17. Compact entfernt ausschließlich bestätigte Sequenzen bis zum Snapshot.
18. Crash an jeder Snapshot-/Manifest-/Compact-Grenze bleibt konsistent.
19. voller Writer-Kanal liefert `state_backpressure`.
20. Read-only Diagnose bleibt unter Backpressure verfügbar.

### Protectoren und Rotation

21. Windows-DPAPI-Roundtrip und falscher Kontext schlägt fehl.
22. macOS-Keychain-Roundtrip und falscher Benutzer schlägt fehl.
23. Linux-Secret-Service-Roundtrip und fehlende Session schlägt fail-closed fehl.
24. kein Protector schreibt Klartext oder Schlüsseldatei in den Store.
25. Key-ID ist nicht geheim und eindeutig auflösbar.
26. Rotation benötigt Owner/Admin, Begründung und Bestätigung.
27. Rotation erstellt vor Änderung ein Backup.
28. Abbruch an jeder Rotationsphase erhält den alten lesbaren Store.
29. fehlender alter Schlüssel startet nur Diagnose.
30. neue Payloads verwenden nach Rotation ausschließlich die neue Key-ID.

### Lifecycle und Adapter

31. deaktivierte Persistenz öffnet keinen Writer.
32. Adapter startet erst nach `Ready`.
33. `RecoveryRequired` verhindert mutierende Adapteraufrufe.
34. abgelaufene Readiness-Lease verhindert die Fachmutation.
35. `RecoveryFailed` lässt nur Diagnose/Wartung zu.
36. zweiter Host-Lifecycle erhält `state_store_locked`.
37. Shutdown sperrt neue Mutationen vor Flush.
38. erfolgreicher Shutdown bestätigt Journal-Flush und Writer-Freigabe.
39. Shutdown-Timeout meldet `state_shutdown_incomplete`.
40. Module Manager und ein zweiter Referenzhost bestehen dieselbe Lifecycle-Suite.

### Sicherheit, Last und Regression

41. Sessions, Permissions, Locks und Message-Payloads fehlen weiterhin im Store.
42. Audit, Diagnose und Metriken redigieren Secrets und Pfade.
43. 16 parallele Clients erzeugen keine Doppelanwendung.
44. 10.000 Idempotenzdatensätze bleiben begrenzt und deterministisch.
45. 50.000 Audit-Einträge respektieren Retention und Reihenfolge.
46. Quoten stoppen vor dauerhaftem Überschreiben gültiger Daten.
47. 24-Stunden-Soak zeigt keine Lock-, Handle- oder unbeschränkten Speicherlecks.
48. wiederholter Crash/Restart erzeugt keine Sequenzlücke.
49. keine neuen MCP-Tools oder Permissions entstehen.
50. vollständige bestehende Regression bleibt grün.

## 11. Implementierungsreihenfolge

1. Delta-Contracts, Event-Registry und In-Memory-Replay.
2. zentrale durable Mutation-Transaktion und Operation-ID-Deduplizierung.
3. Snapshot-Schwellen, paralleler Snapshot und sichere Kompaktierung.
4. Migration des Phase-9-Checkpoint-Events.
5. macOS-Keychain- und Linux-Secret-Service-Protectoren.
6. Key-ID, Rotation und Offline-Migration.
7. app-neutraler Lifecycle und Readiness-Lease.
8. Module-Manager-Migration auf den gemeinsamen Lifecycle.
9. Conformance-Kit, Last-, Crash- und Soak-Suite.
10. vollständige Regression und getrennte Implementierungs-PRs je Inkrement.

## 12. Abnahmekriterien

Phase 10 ist abgeschlossen, wenn:

- normale Mutationen nur kleine typisierte Delta-Events schreiben,
- jeder bestätigte interne Zustand exakt einmal replaybar ist,
- Snapshot und Compact die definierte Schwellenpolitik einhalten,
- Windows, macOS und Linux entweder einen nativen Protector nutzen oder klar fail-closed bleiben,
- mindestens Module Manager und ein zweiter Referenzhost denselben Lifecycle verwenden,
- kein Adapter vor `Ready` mutieren kann,
- Rotation und Migration an jeder Fehlergrenze den alten Zustand erhalten,
- Conformance-, Last-, Crash-, Soak- und bestehende Regressionstests grün sind,
- Betriebsdokumentation Aktivierung, Schlüsselverlust, Backup, Repair und Shutdown beschreibt.
