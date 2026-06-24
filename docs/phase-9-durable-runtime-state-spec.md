# Phase 9 — AIR Durable Runtime State & Crash Recovery: Spezifikation

> Status: fachlich freigegeben; 9.1 bis 9.3 abgeschlossen
> Scope: lokale, geschützte Persistenz des Orchestrierungszustands; kein MCP, keine Cloud

## 1. Ausgangslage und Ziel

Phase 8 stellt Messaging, Scheduling, Ressourcenwahl und kontrollierte Adapter bereit.
Der operative Zustand liegt jedoch vollständig im Prozessspeicher. Ein App-Absturz oder
Neustart verliert insbesondere Queue-Einträge, Task-Zustände, Budgetverbrauch,
Reservationshistorie und Idempotenzinformationen.

Phase 9 führt einen host-neutralen State Store mit versionierten Snapshots und einem
geordneten Änderungsjournal ein. Nach einem Neustart stellt AIR ausschließlich
nachweisbar konsistenten Zustand wieder her und normalisiert unsichere Laufzeitzustände
fail-closed. Bereits gestartete Tool-Arbeit wird niemals automatisch wiederholt.

## 2. Nicht-Ziele

Phase 9 implementiert ausdrücklich nicht:

- Cloud-Synchronisation, Mehrknoten-Konsens oder verteilte Locks,
- externe Datenbanken, Message Broker oder Remote-Control-Plane,
- neue MCP-Tools oder neue MCP-Permissions,
- Persistenz von Bearer-Tokens, API-Keys, Private Keys oder Zugangsdaten,
- Wiederherstellung aktiver Sessions, Permissions oder Workspace-Locks,
- automatische Wiederholung einer vor dem Absturz laufenden Tool-Ausführung,
- dauerhafte Session-Mailboxen oder Offline-Messaging,
- persistente Modell-/Vendor-Profile,
- Persistenz freier Blackboard-/Memory-Inhalte ohne eigene Schutzklassifizierung,
- Backup-Upload, Gerätewechsel oder Benutzerkonten-Synchronisation.

## 3. Inkremente

| Inkrement | Inhalt | Status |
|---|---|---|
| 9.1 | State-Store-Contracts, Schema, Snapshot und Journal | abgeschlossen |
| 9.2 | Durable Tasks und Execution Queue mit Recovery | abgeschlossen |
| 9.3 | Durable Budgets, Reservationshistorie und Idempotenz | abgeschlossen |
| 9.4 | Audit, lokale Diagnose und kontrollierte Wartung | spezifiziert |

Jedes Inkrement erhält einen eigenen Commit-Checkpoint. Keine Persistenz wird aktiviert,
bevor Schema-, Korruptions- und Recovery-Tests des betreffenden Inkrements grün sind.

## 4. Architekturgrenze

AIR definiert Zustand, Reihenfolge und Recovery-Regeln. Ein Host stellt ausschließlich
Speicher und Schutzmechanismen bereit.

### 4.1 Contracts

`AAIA.Air.Contracts` erhält BCL-only Interfaces:

- `IAiRuntimeStateStore`: exklusives Öffnen, Snapshot laden/schreiben, Journal lesen/
  anhängen, Flush, Compact und Quarantine,
- `IAiStateProtector`: geschützte Payload-Bytes versiegeln und entsiegeln,
- `AiRuntimeStateManifest`: Schema, Store-ID, Runtime-Instanz, Sequenz, Zeitstempel,
  Prüfsumme und Feature-Flags,
- typisierte Snapshot- und Journal-Contracts ohne Host- oder Vendor-Typen.

Der AIR-Kern kennt weder Dateipfade noch SQLite, DPAPI, Keychain oder Betriebssystem-
APIs. Der Module Manager implementiert später einen lokalen Host-Adapter. Andere Apps
können denselben Contract mit eigener Speicherung erfüllen.

### 4.2 Single Writer

Pro Store darf genau eine Runtime-Instanz schreibend geöffnet sein. Ein zweiter Writer
wird abgewiesen. Read-only Diagnosezugriff darf nur einen konsistenten, abgeschlossenen
Snapshot lesen und keine Journalposition verändern.

Der Host hält einen exklusiven, prozessübergreifenden Store-Lock. Ein verwaister Lock
wird nicht anhand des Alters allein entfernt; erst der sichere Nachweis, dass der
Owner-Prozess nicht mehr existiert, erlaubt die Übernahme.

## 5. Persistenzmodell

### 5.1 Snapshot

Ein Snapshot enthält den letzten vollständig bestätigten Zustand zu Sequenz `N`:

- Task-Metadaten, Schritte und Status,
- Execution Requests, Priorität, Attempts, Deferrals und letzter Fehlercode,
- Budgetdefinitionen sowie `Spent` und `Reserved`,
- Reservationshistorie und Abrechnungszustand,
- begrenzte Idempotenzdatensätze,
- redigierte Audit-Einträge,
- Recovery-Metadaten und Schema-Version.

Nicht enthalten sind Sessions, Session-Permissions, Locks, Cancellation Tokens,
Tool-Handler, Host-Instanzen, Telemetrie-Samples und Secrets.

### 5.2 Journal

Jede persistenzrelevante Mutation erzeugt einen typisierten Journal-Eintrag mit:

- monotoner `Sequence`,
- stabiler `OperationId`,
- `OccurredAtUtc`,
- Event-Typ und Schema-Version,
- geschützter Payload oder redigierten Metadaten,
- Prüfsumme über Header und Payload.

Extern bestätigte Mutationen werden erst als erfolgreich zurückgegeben, wenn ihr
Journal-Eintrag dauerhaft geflusht wurde. Dazu gehören Enqueue, Cancel, Budgetänderung,
Reservation, Commit/Release/Expiry und das erstmalige Speichern einer Idempotency-ID.

Reine Telemetrie, Session-Touch, Tool-Listing und UI-Snapshots werden nicht journalisiert.

### 5.3 Snapshot und Kompaktierung

- Standardmäßig wird nach 1.000 Journal-Einträgen oder 10 Minuten ein Snapshot erzeugt.
- Schreiben erfolgt `temp → flush → checksum verify → atomic replace`.
- Das Journal wird erst nach bestätigtem Snapshot und erneutem Manifest-Flush bis zur
  Snapshot-Sequenz kompaktiert.
- Ein Absturz während Snapshot oder Kompaktierung muss entweder den alten oder den neuen
  vollständigen Zustand liefern, niemals eine Mischung.
- Dateianzahl und Gesamtgröße besitzen konfigurierbare harte Grenzen.

## 6. Schutzklassen und Datenminimierung

### 6.1 Steuerdaten

IDs, Zustände, Prioritäten, Zähler, Zeitpunkte und Reason-Codes dürfen im lokalen Store
liegen. Freie Fehlertexte werden vor dem Schreiben mit derselben Secret-Maskierung wie
Audit behandelt und auf 2.000 Zeichen begrenzt.

### 6.2 Geschützte Payloads

Task-Schritt-Inputs können Quelltext oder vertrauliche Projektinformationen enthalten.
Sie werden nur geschrieben, wenn ein `IAiStateProtector` verfügbar ist. Der Protector
liefert authentifizierte Verschlüsselung mit Schlüsselbindung an den lokalen Benutzer.
Ohne Protector darf AIR Metadaten persistieren, aber keine Task-Payload annehmen, deren
spätere Wiederherstellung Payload-Persistenz erfordert.

Private Keys, Passwörter, Bearer-/JWT-Tokens und erkannte Secret-Zuweisungen werden auch
verschlüsselt nicht persistiert. Die bestehende Secret Policy läuft vor dem Protector.

### 6.3 Explizit flüchtige Daten

- MCP-Bridge-Token und Client-Verbindungsdaten,
- aktive Sessions, ausgehandelte Capabilities und Permissions,
- Workspace-Locks und Cancellation Tokens,
- Message-Inboxen und Nachrichten-Payloads,
- rohe Resource-Telemetrie,
- Tool-Ergebnis-Payloads,
- unklassifizierte Blackboard-/Memory-Werte.

## 7. Recovery-Ablauf

1. Store exklusiv öffnen.
2. Manifest und Snapshot-SHA-256 prüfen.
3. Unterstützte Schema-Version verifizieren.
4. Journal ab `Snapshot.Sequence + 1` strikt in Sequenz einlesen.
5. Prüfsumme und OperationId jedes Eintrags prüfen.
6. Einträge deterministisch und idempotent replayen.
7. Laufzeitzustände gemäß Abschnitt 8 normalisieren.
8. Recovery-Report und neuen Recovery-Checkpoint dauerhaft schreiben.
9. Erst danach Runtime für Adapter und Scheduler freigeben.

Während Recovery sind MCP-Server, Scheduler-Assignment und mutierende UI-Aktionen
deaktiviert. Read-only UI zeigt ausschließlich Recovery-Status und Fehlerbericht.

## 8. Zustandsnormalisierung nach Neustart

### 8.1 Sessions und Locks

Sessions und Workspace-Locks werden niemals wiederbelebt. Neue Verbindungen erzeugen
neue Sessions und durchlaufen erneut Capability- und Permission-Prüfung.

### 8.2 Tasks und Executions

| Persistierter Zustand | Zustand nach Recovery | Regel |
|---|---|---|
| `Pending` / `Queued` | unverändert | darf später normal zugewiesen werden |
| `Claimed` / `Leased` | `Pending` / `Queued` | Owner und Lease löschen; Recovery zählt nicht als Attempt |
| `InProgress` / `Running` | `RecoveryRequired` | kein automatischer Retry und kein Tool-Aufruf |
| `Cancelling` | `RecoveryRequired` | unbekannt, ob Seiteneffekt bereits erfolgte |
| `Completed` / `Failed` / `Cancelled` | unverändert | terminal bleibt terminal |

`RecoveryRequired` wird als neuer expliziter Task-/Execution-Zustand eingeführt. Nur ein
lokaler Owner/Admin darf ihn nach Sichtprüfung auf `Failed` setzen oder daraus einen
neuen Task/Execution Request erzeugen. Die alte Execution wird niemals wiederverwendet.

### 8.3 Reservations und Budgets

- `Committed` bleibt verbucht und wird nie erneut committed.
- `Released` und `Expired` bleiben terminal.
- offene `Reserved`-Einträge werden beim Recovery einmalig als `Released` mit Reason
  `runtime_recovery` abgeschlossen.
- Zugehöriges `Reserved`-Budget wird exakt einmal freigegeben.
- `Spent` wird ausschließlich aus bestätigten Commits rekonstruiert.
- Externe Telemetrie muss nach Neustart neu geliefert werden, bevor eine Ressource
  wieder wählbar ist.

### 8.4 Idempotenz

Idempotenzdatensätze enthalten Session-unabhängig einen stabilen Client-Fingerprint,
Operationstyp, Idempotency-ID, Input-Fingerprint, Resultat-ID und Ablaufzeit. Keine
vollständige Request- oder Result-Payload wird gespeichert.

Standard-TTL: 24 Stunden, maximal 10.000 Datensätze. Ein wiederholter Request nach
Neustart liefert dieselbe Resultat-ID; ein anderer Input unter derselben ID bleibt ein
`idempotency_conflict`.

## 9. Schema-Versionierung und Migration

- Manifest und jeder Journal-Eintrag tragen eine ganzzahlige `SchemaVersion`.
- Reader akzeptieren nur explizit registrierte Versionen.
- Migrationen laufen offline gegen eine Kopie und sind deterministisch/idempotent.
- Vor einer Migration wird ein unveränderter Backup-Snapshot angelegt.
- Downgrade ist nicht implizit erlaubt.
- Unbekannte neuere Version: Store read-only öffnen, verständlichen Fehler melden,
  keine Datei verändern.
- Unbekannter Event-Typ innerhalb einer angeblich unterstützten Version gilt als
  Korruption und stoppt die schreibende Runtime.

## 10. Korruption und Fail-Closed-Verhalten

- Ein unvollständiger letzter Journal-Eintrag darf als Crash-Tail quarantänisiert werden,
  wenn alle vorherigen Sequenzen und Checksummen gültig sind.
- Lücke, doppelte Sequenz, ungültige Prüfsumme oder beschädigter Snapshot in der Mitte
  führt zu `RecoveryFailed`; kein Scheduler und kein MCP-Write wird gestartet.
- Der Store wird niemals still auf einen leeren Zustand zurückgesetzt.
- Diagnose nennt Store-ID, letzte gültige Sequenz und Reason-Code, aber keine Payload.
- Reparatur ist eine explizite lokale Admin-Aktion mit Backup und Audit.
- Ein fehlgeschlagener Reparaturversuch verändert das Original nicht.

Stabile Reason-Codes:

- `state_store_locked`
- `state_schema_unsupported`
- `state_snapshot_corrupt`
- `state_journal_gap`
- `state_journal_corrupt`
- `state_journal_checksum_failed`
- `state_journal_event_unknown`
- `state_protector_unavailable`
- `state_payload_rejected`
- `state_recovery_required`
- `state_quota_exceeded`
- `state_store_disabled`

## 11. Betriebsgrenzen

- Persistenz ist standardmäßig deaktiviert, bis Store und Protector konfiguriert sind.
- Der lokale Store liegt außerhalb von Projektordnern und Git-Repositories.
- Verzeichnis und Dateien sind nur für den aktuellen OS-Benutzer les-/schreibbar.
- Standardlimit: 100 MiB pro Store; einzelne geschützte Payloads über 1 MiB werden
  abgewiesen.
- Audit-Aufbewahrung: maximal 30 Tage oder 50.000 Einträge, zuerst gilt die engere Grenze.
- UTC für alle Zeitpunkte; monotone Sequenzen für Reihenfolge.
- Ein Speicherfehler darf nicht als erfolgreiche Mutation bestätigt werden.
- Persistenz-Backpressure blockiert neue Mutationen kontrolliert, aber keine read-only
  Diagnose.

## 12. UI und Adapter

Die lokale UI erhält:

- Store-Status, Schema-Version, Größe, Sequenz und letzten Flush,
- Recovery-Zustand und redigierten Recovery-Report,
- explizite Aktionen für Backup, Compact und Repair mit Owner/Admin, Begründung,
  Bestätigung und Audit,
- Liste `RecoveryRequired` mit Aktion „als fehlgeschlagen abschließen“ oder
  „neuen Retry erzeugen“.

MCP erhält in Phase 9 keine neuen Tools. Solange Recovery nicht `Ready` ist, lehnt der
Adapter mutierende Aufrufe mit `runtime_recovering` bzw. `runtime_recovery_failed` ab.
Read-only Status darf nur redigierte Betriebsdaten liefern.

## 13. Implementierungsreihenfolge

1. Contracts, Schema-Manifest und in-memory Test-Store.
2. Snapshot-/Journal-Codec mit Prüfsummen und Größenlimits.
3. Single-Writer, Flush-Semantik und Crash-Injection-Tests.
4. Task-/Execution-Persistenz und `RecoveryRequired`.
5. Budget-/Reservations-Replay und Recovery-Release.
6. persistente, begrenzte Idempotenz.
7. geschützter lokaler Module-Manager-Store und Protector.
8. Audit, Recovery-UI und lokale Wartungsaktionen.
9. vollständige Regression und eigener Implementierungs-PR.

## 14. Pflicht-Tests

### Store, Schema und Schutz

1. Persistenz ist ohne explizite Konfiguration deaktiviert.
2. zweiter Writer wird prozessübergreifend abgewiesen.
3. Snapshot-Replace liefert nach Crash vollständig alt oder vollständig neu.
4. unterstützte Schema-Version lädt deterministisch.
5. neuere unbekannte Version bleibt unverändert und read-only.
6. unbekannter Event-Typ stoppt Recovery.
7. Secret Policy läuft vor dem Protector.
8. geschützte Payload ohne Protector wird abgewiesen.
9. Store-Dateirechte sind auf den aktuellen Benutzer begrenzt.
10. Größen- und Gesamtquoten werden vor dauerhaftem Schreiben erzwungen.

### Journal und Konsistenz

11. Sequenzen sind unter parallelen Mutationen eindeutig und lückenlos.
12. bestätigte Mutation ist nach sofortigem Prozessabbruch wieder vorhanden.
13. ungeflushtes Crash-Tail wird sicher quarantänisiert.
14. Prüfsummenfehler in der Mitte startet keine schreibende Runtime.
15. Sequenzlücke startet keine schreibende Runtime.
16. Replay derselben OperationId verändert Zustand nur einmal.
17. Kompaktierung verliert keine gleichzeitig bestätigte Mutation.
18. Speicherfehler liefert Fehler statt erfolgreicher Tool-Antwort.

### Task- und Execution-Recovery

19. Pending/Queued bleibt ausführbar.
20. Claim/Lease wird ohne Attempt-Verbrauch freigegeben.
21. Running wird `RecoveryRequired` und niemals automatisch gestartet.
22. Cancelling wird `RecoveryRequired`.
23. terminale Zustände bleiben unverändert.
24. Retry erzeugt neue Task-/Execution-IDs und lässt Original unverändert.
25. ohne lokale Owner/Admin-Autorisierung ist Recovery-Entscheidung gesperrt.
26. Scheduler bleibt bis abgeschlossenem Recovery deaktiviert.

### Ressourcen, Budget und Idempotenz

27. offene Reservation wird genau einmal mit `runtime_recovery` released.
28. Reserved-Budget wird genau einmal freigegeben.
29. Commit bleibt spent und wird nicht doppelt gebucht.
30. Telemetrie gilt nach Neustart als unbekannt/stale.
31. gleiche Idempotency-ID liefert nach Neustart dieselbe Resultat-ID.
32. anderer Input unter gleicher ID bleibt Konflikt.
33. TTL und Größenlimit entfernen nur abgelaufene/älteste Einträge deterministisch.

### Sicherheit, UI und Regression

34. Sessions, Permissions und Locks werden nicht wiederhergestellt.
35. Message-Payloads, Tokens und Tool-Ergebnisse fehlen im Store.
36. Recovery-Report und Audit redigieren Secrets.
37. MCP-Mutationen bleiben während Recovery gesperrt.
38. Wartungsaktionen benötigen Owner/Admin, Begründung und Bestätigung.
39. Crash-Injection an jeder Write-/Flush-/Replace-Grenze ergibt konsistenten Zustand.
40. bestehende 186 Tests bleiben grün.

## 15. Abnahmekriterien

Phase 9 ist abgeschlossen, wenn:

- jeder bestätigte persistenzrelevante Zustand einen Crash überlebt,
- keine laufende Tool-Arbeit automatisch wiederholt wird,
- Budget und Reservationszustand nach Recovery exakt konsistent sind,
- unbekannte oder korrupte Stores fail-closed bleiben,
- keine Secrets, Sessions, Permissions oder Locks persistiert werden,
- alle 40 Pflichtfälle und die vollständige Regression grün sind,
- Handoff und Betriebsdokumentation den implementierten Store exakt beschreiben.

## 16. Implementierungsstand

Umgesetzt:

- BCL-only Contracts für Manifest, Snapshot, Journal, Store-Session und Protector,
- stabile State-Store-Reason-Codes und standardmäßig deaktivierte Optionen,
- sessiongebundener `AiInMemoryRuntimeStateStore` als isolierbarer Referenz-Store,
- Single-Writer mit parallelem read-only Diagnosezugriff,
- defensive Kopien, Schema-/UTC-/Checksum-Formatprüfung, lückenlose Sequenzen,
- Flush-Checkpoint, Snapshot-Grenze, Kompaktierung, Quota und Quarantäne,
- kanonischer Big-Endian-Binärcodec für Snapshot und Journal,
- echte SHA-256-Erzeugung und Fixed-Time-Verifikation über Header und Payload,
- striktes UTF-8, Record-/Payload-Limits sowie Erkennung von Truncation,
  nachlaufenden Bytes, unbekannten Events und Manipulation,
- lokaler Module-Manager-Datei-Store außerhalb von Projekt-/Git-Verzeichnissen,
- exklusiver Writer-Lock bei parallelem read-only Diagnosezugriff,
- atomisches `temp → flush → verify → replace` für Manifest, Snapshot und Kompaktierung,
- append-only Journal mit Disk-Flush, Crash-Tail-Quarantäne und lückenloser Recovery,
- explizite Unix-Owner-Rechte; unter Windows usergebundener LocalAppData-Standardpfad,
- Fault-Injection an Replace- und Journal-Grenzen sowie fail-closed Korruptionsprüfung,
- persistierbare Task-/Execution-DTOs ohne Sessions, Leases, Handler oder Tool-Ergebnisse,
- geschützte Task-Inputs mit kontextgebundener Entsiegelung und Secret-Prüfung vor dem Protector,
- deterministischer Export/Import sowie fail-closed Prüfung vor jeder Runtime-Mutation,
- Recovery-Normalisierung für Claims, Leases, laufende und abbrechende Ausführungen,
- explizites `RecoveryRequired` ohne automatischen Tool-Aufruf oder Wiederverwendung alter IDs,
- Scheduler-Sperre bis zur Auflösung aller unsicheren Executions,
- host-injizierte Owner/Admin-Autorisierung für manuelles Fail oder Retry,
- persistierbare Budgetdefinitionen und sessionfreie Reservationshistorie,
- Rekonstruktion von `Spent` ausschließlich aus bestätigten Commits mit Konsistenzprüfung,
- exakt-einmal Recovery-Release offener Reservations samt vollständiger Freigabe von `Reserved`,
- expliziter Settlement-Reason-Code `runtime_recovery`; Profile und Telemetrie bleiben flüchtig,
- stabile, clientgebundene Idempotenz über SHA-256-Input-Fingerprints und Resultat-IDs,
- 24-Stunden-TTL, deterministische Begrenzung auf 10.000 Einträge und keine Request-/Result-Payloads,
- 78 neue Phase-9-Tests; vollständige Regression 264/264 grün.

Noch offen:

- Audit, lokale Diagnose und kontrollierte Wartung (Phase 9.4),
- Runtime-Startup-/Journal-Koordination, lokaler Protector, UI und Wartungsaktionen.

Persistenz bleibt bis zu diesen Schritten deaktiviert und ist nicht mit
`AiRuntimeService` verbunden.
