# Phase 8.4 — AIR Adapter, MCP und UI: Spezifikation

> Status: spezifiziert, fachliche Freigabe und Implementierung offen
> Scope: kontrollierte Freigabe bestehender Phase-8-Funktionen; keine neue Orchestrierungslogik

## 1. Ziel

Phase 8.4 macht Messaging, Execution Queue und Resource Manager kontrolliert für
Adapter und die Module-Manager-Oberfläche sichtbar. Alle fachlichen Entscheidungen
bleiben im AIR-Kern. Adapter übersetzen ausschließlich Transportdaten, die UI spricht
über eine lokale Anwendungsfassade mit der Runtime und MCP bleibt standardmäßig ohne
die neuen mutierenden Rechte.

Die Trennung bleibt verbindlich:

- Scheduler: wer einen Task wann übernimmt.
- Resource Manager: wo Kapazität reserviert wird.
- Runtime-Sicherheitskette: ob ein konkreter Tool-Aufruf zulässig ist.
- Adapter: wie ein autorisierter Aufruf transportiert wird.
- UI: was ein lokaler Mensch beobachten oder ausdrücklich freigeben kann.

## 2. Nicht-Ziele

Phase 8.4 führt ausdrücklich nicht ein:

- neue Scheduling-, Ranking-, Budget- oder Messaging-Fachlogik,
- direkte Tool-Ausführung aus MCP-, UI- oder Host-Adaptern,
- Vendor-/Modell-Sonderlogik,
- automatische Permission- oder Budgeterhöhung,
- Signatur- oder Marketplace-Freigaben,
- öffentliche Netzwerk-Listener, Cloud-Control-Plane oder externen Message Broker,
- Secrets, API-Keys oder Zugangsdaten in Nachrichten, Profilen oder Anzeigen,
- MCP-Werkzeuge zum Registrieren von Ressourcen, Schreiben von Telemetrie,
  Ändern von Budgets oder Steuern von Reservationszuständen.

## 3. Permissions

Die bestehenden Permissions werden um fachlich getrennte Rechte ergänzt:

| Permission | Erlaubt | Nicht erlaubt |
|---|---|---|
| `Read` | eigene Nachrichten, eigene Executions sowie redigierte Ressourcenübersicht lesen | Nachrichten senden, Executions erzeugen/abbrechen, Ressourcen verändern |
| `Collaborate` | Nachrichten als aktuelle Session senden und eigene Nachrichten bestätigen | Absender oder fremde Inbox vorgeben |
| `Schedule` | Executions für vorhandene Tasks einreihen und eigene wartende/laufende Executions abbrechen | Lease/Session erzwingen, Handler starten, Prioritätsgrenzen umgehen |
| `ManageResources` | ausschließlich lokaler administrativer Host: Profile aktivieren/deaktivieren und Budgets konfigurieren | MCP-Nutzung, automatische Budgeterhöhung, Reservation manuell erzwingen |

Regeln:

1. Keine neue Permission ist Bestandteil der MCP-Defaults.
2. `Read` impliziert weder `Collaborate`, `Schedule` noch `ManageResources`.
3. `ManageResources` wird in Phase 8.4 nicht über MCP vergeben oder ausgewertet.
4. `Sign` und `Marketplace` bleiben hart gesperrt.
5. Tool-Capability, Permission, Lock, Approval, Audit und Events werden weiterhin
   ausschließlich durch `AiRuntimeService` geprüft.

Die MCP-Konfiguration erhält explizite, standardmäßig deaktivierte Schalter
`AllowCollaboration`, `AllowScheduling` und `AllowResourceRead`. Einen Schalter für
MCP-Ressourcenverwaltung gibt es nicht.

## 4. MCP-Oberfläche

### 4.1 Werkzeugmatrix

| Tool | Permission | Wirkung | Risikostufe |
|---|---|---|---|
| `aaia.message.inbox` | `Read` | eigene Inbox lesen | Green |
| `aaia.message.send` | `Collaborate` | Nachricht als aktuelle Session senden | Yellow |
| `aaia.message.acknowledge` | `Collaborate` | eigene Nachricht bestätigen | Yellow |
| `aaia.execution.list` | `Read` | eigene Executions lesen | Green |
| `aaia.execution.get` | `Read` | eigene Execution lesen | Green |
| `aaia.execution.enqueue` | `Schedule` | vorhandenen Task einreihen | Yellow |
| `aaia.execution.cancel` | `Schedule` | eigene Execution abbrechen | Orange |
| `aaia.resource.list` | `Read` + `AllowResourceRead` | redigierte Profile und Zustand lesen | Green |
| `aaia.resource.status` | `Read` + `AllowResourceRead` | aggregierte Kapazität, Last und Budgetstatus lesen | Green |

Nicht als MCP-Tools registriert werden:

- Lease, Claim, Start, Complete oder Fail einer Execution,
- Profilregistrierung oder -löschung,
- Telemetrie-Updates,
- Budgetänderungen,
- Reserve, Commit, Release oder Expire,
- freie Resource-Decision-Requests außerhalb einer Scheduler-Execution.

### 4.2 Identität und Sichtbarkeit

- Der Adapter bezieht die Session aus dem authentifizierten MCP-Kontext. Session-,
  Sender- oder Owner-IDs werden nicht aus Client-Payloads übernommen.
- Eine MCP-Session liest nur ihre eigene Inbox und nur von ihr erzeugte Executions.
- Broadcast benötigt `Collaborate`; die Runtime bestimmt die Empfänger.
- Ein Client darf keine Ziel-Session für Scheduling, kein Lease und keine konkrete
  Worker-Session erzwingen.
- Ressourcenansichten enthalten technische `ResourceId`, Kind, Capabilities,
  Health und aggregierte Auslastung. `ProviderId`, Kostendetails, interne
  Ablehnungsdetails und freie Metadaten werden für MCP redigiert.

### 4.3 Transportgrenzen

- Der bestehende Listener bleibt auf `127.0.0.1` und benötigt Bearer-Token.
- Tokenrotation beendet die Gültigkeit des vorherigen Tokens.
- Eingaben erhalten feste Größen-, Längen- und Mengenlimits; ungültige Enums,
  Prioritäten und Zeitwerte werden vor der Runtime abgewiesen.
- Mutationen besitzen eine clientseitig lieferbare Idempotency-ID. Wiederholungen
  dürfen weder Nachrichten noch Executions doppelt erzeugen.
- Adapterfehler geben stabile Codes zurück, aber keine Stacktraces, Pfade oder Secrets.
- Jede Mutation erzeugt Audit- und Runtime-Events mit Session- und Korrelations-ID.

## 5. Adaptergrenzen

`AAIA.Air.Mcp` bleibt ein dünner Transportadapter. Er darf:

- MCP-Schemas auf Contracts abbilden,
- authentifizierte Sessions auflösen,
- sichtbare Tools anhand expliziter Optionen und Runtime-Permissions filtern,
- Runtime-Ergebnisse in redigierte Transportantworten übersetzen.

Er darf nicht:

- Queue-, Messaging-, Resource- oder Budgetzustand selbst halten,
- Scheduler- oder Resource-Entscheidungen nachbauen,
- Permissions, Rollen, Capabilities oder Owners ableiten,
- Runtime-Sicherheitsprüfungen umgehen,
- Tool-Handler direkt aufrufen.

Host-Adapter dürfen Ressourcenprofile und Telemetrie über schmale AIR-Contracts
liefern. Sie verwalten Provider-Verbindungen und Secrets außerhalb der AIR-Contracts.
Die Runtime erhält nur normalisierte, herstellerneutrale Daten. Adapter starten oder
stoppen keine Infrastruktur als Nebenwirkung einer Auswahlentscheidung.

## 6. Module-Manager-UI

Die UI verwendet `AiRuntimeConnectorPanel` bzw. eine lokale Anwendungsfassade direkt;
sie ruft nicht den eigenen MCP-Endpunkt zurück.

### 6.1 Beobachtung

Die AIR-Sektion ergänzt read-only Ansichten für:

- Inbox und Zustell-/Bestätigungsstatus,
- Execution Queue mit Zustand, Priorität, Owner und letztem Fehlercode,
- Ressourcen mit Health, Kapazität, Reservationszahl und redigiertem Budgetstatus.

Listen sind begrenzt, paginiert bzw. virtuell und werden über Events oder gedrosselte
Snapshots aktualisiert. Secrets und unredigierte Nachrichteninhalte erscheinen nicht
in Diagnoseexporten.

### 6.2 Explizite Aktionen

- MCP-Freigaben für Collaboration, Scheduling und Resource Read sind getrennte,
  standardmäßig ausgeschaltete Schalter.
- Das Abbrechen einer Execution benötigt eine lokale Bestätigung mit Execution- und
  Task-ID; fremde Executions benötigen lokale Administratorrechte.
- Ressourcen aktivieren/deaktivieren und Budgets ändern benötigen lokale
  Administratorrechte, Bestätigung, Begründung und Audit-Eintrag.
- Die UI darf keine Reservation manuell auswählen, erzwingen, committen oder lösen.
- Profilregistrierung und Telemetrie bleiben Host-getrieben. Zugangsdaten werden
  ausschließlich in der jeweiligen Host-Konfiguration verwaltet.
- Keine UI-Aktion erhöht verdeckt Permissions, Budgets oder MCP-Freigaben.

## 7. Sicherheitsinvarianten

1. Alle MCP-Mutationen laufen mit der authentifizierten Session durch die Runtime.
2. Sender-, Owner- und Session-IDs sind serverseitig gebunden und nicht fälschbar.
3. Cancellation prüft Owner oder lokale administrative Autorisierung.
4. Der Scheduler bleibt der einzige Erzeuger von Leases und Execution-Übergängen.
5. Der Resource Manager bleibt der einzige Erzeuger atomarer Reservationsentscheidungen.
6. Adapter führen niemals Tool-Handler aus; die vollständige Sicherheitskette bleibt aktiv.
7. Neue MCP-Fähigkeiten sind nach Installation, Update und Tokenrotation deaktiviert.
8. Resource- und Budgetdaten werden nach Zieloberfläche minimal offengelegt.
9. Vendor-Namen beeinflussen weder Sichtbarkeit, Ranking noch Autorisierung.
10. Fehler, Audit und Telemetrie enthalten keine Secrets oder vollständigen Tokens.

## 8. Implementierungsreihenfolge

1. Contracts: neue Permissions, Sichtbarkeits-/DTO-Verträge und stabile Fehlercodes.
2. Tests für Permission-Defaults, Ownership, Redaction und Tool-Sichtbarkeit.
3. Read-only MCP-Werkzeuge für Inbox, Executions und Ressourcenstatus.
4. Collaboration-Werkzeuge mit Idempotenz und Audit.
5. Scheduling-Werkzeuge für Enqueue und Cancel; keine Lease-/Run-Werkzeuge.
6. Lokale UI-Fassade und read-only Ansichten.
7. Bestätigte lokale Admin-Aktionen für Cancel, Resource Enable/Disable und Budgets.
8. Vollständige Regression und eigener Implementierungs-PR.

Read-only und mutierende Schritte werden getrennt committet. Eine nachfolgende Stufe
wird erst begonnen, wenn die Sicherheits- und Isolationstests der vorherigen grün sind.

## 9. Pflicht-Tests

### Permissions und Defaults

1. Alle drei neuen MCP-Schalter sind standardmäßig deaktiviert.
2. `Read` macht kein mutierendes Phase-8-Tool sichtbar.
3. Jedes Tool ist ohne seine exakte Permission unsichtbar und nicht aufrufbar.
4. `ManageResources` kann nicht über MCP konfiguriert oder genutzt werden.
5. `Sign` und `Marketplace` bleiben gesperrt.

### Session- und Datentrennung

6. Sender- und Owner-ID können nicht per Payload gefälscht werden.
7. Eine Session kann keine fremde Inbox lesen oder Nachricht bestätigen.
8. Eine Session kann keine fremde Execution lesen oder abbrechen.
9. Broadcast schließt die Sender-Session weiterhin aus.
10. MCP-Ressourcenantworten redigieren Provider-, Kosten- und Metadatenfelder.

### MCP-Mutationen

11. Doppelte Idempotency-ID erzeugt nur eine Nachricht.
12. Doppelte Idempotency-ID erzeugt nur eine Execution.
13. Enqueue akzeptiert nur vorhandene, zulässige Tasks.
14. MCP kann weder Lease noch Worker-Session oder Reservation erzwingen.
15. Cancel durch Owner propagiert korrekt in Scheduler und Task.
16. Resource-Mutationswerkzeuge sind nicht registriert.

### Adapter und Sicherheitskette

17. Jeder mutierende Handler delegiert an die Runtime und schreibt keinen eigenen Zustand.
18. Runtime-Permission-Fehler können nicht durch Tool-Caching oder Optionsänderung umgangen werden.
19. Token fehlt/falsch/rotiert wird abgewiesen; Listener bleibt ausschließlich lokal.
20. Größen- und Mengenlimits werden vor Zustandsänderung erzwungen.
21. Fehlerantworten und Audit enthalten weder Secrets noch Stacktraces.
22. Parallele Sessions bleiben bei Inbox, Execution und Idempotenz isoliert.

### UI und Host

23. Die UI verwendet die lokale Fassade und keinen MCP-Loopback.
24. Neue Freigabeschalter bleiben nach Erststart und Migration aus.
25. Cancel, Resource Enable/Disable und Budgetänderung benötigen Bestätigung und Audit.
26. Ohne lokale Administratorrechte sind Ressourcen- und Budgetmutationen gesperrt.
27. UI-Snapshots sind begrenzt und blockieren den Runtime-Eventpfad nicht.
28. Host-Telemetrie kann keine Permissions, Sessions oder Scheduler-Owner ändern.

### Regression

29. Die bestehenden 161 Tests bleiben grün.
30. Neue Tests decken parallele Clients, wiederholte Requests und Neustart-Defaults ab.

## 10. Abnahmekriterien

Phase 8.4 ist erst abgeschlossen, wenn:

- die Permission-Matrix exakt umgesetzt und standardmäßig geschlossen ist,
- keine nicht spezifizierten Phase-8-Mutationswerkzeuge über MCP existieren,
- UI und Adapter keine AIR-Fachlogik duplizieren,
- Ownership, Idempotenz, Redaction und lokale Bestätigungen getestet sind,
- die vollständige Regression grün ist,
- Dokumentation und Handoff den tatsächlich implementierten Stand wiedergeben.
