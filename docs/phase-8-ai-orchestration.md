# Phase 8 — AI Collaboration & Orchestration

## Ziel

Phase 8 ergänzt die AIR um herstellerneutrale Zusammenarbeit und Ausführungsplanung.
Die bestehende Sicherheitskette für Tools bleibt unverändert; Messaging, Scheduling
und Ressourcenwahl dürfen sie weder ersetzen noch umgehen.

## Inkremente

| Inkrement | Inhalt | Status |
|---|---|---|
| 8.1 | Sessiongebundener Messaging-Bus | abgeschlossen |
| 8.2 | Execution Queue und Scheduler | abgeschlossen |
| 8.3 | Resource Manager für Capabilities, Kosten- und Lastprofile | implementiert |
| 8.4 | Adapter-/MCP-Oberfläche und UI | implementiert; PR-Abnahme offen |

## 8.1 — Messaging

### Akzeptanzkriterien

- Nur aktive Sessions dürfen Nachrichten senden und direkt empfangen.
- Der Bus erzeugt Nachrichten selbst; ein Client kann Sender-ID, Message-ID und
  Zeitstempel nicht fälschen.
- Empfänger sind eine aktive Session oder `broadcast`.
- Broadcast wird an alle anderen aktiven Sessions zugestellt.
- Inboxen sind thread-safe und pro Empfänger begrenzt.
- Offensichtliche Private Keys, Bearer-/JWT-Tokens und Secret-Zuweisungen werden blockiert.
- Nachrichten bleiben bis zur Verdrängung im Speicher und können bestätigt werden.
- Zustellung und Bestätigung erzeugen Runtime-Events.
- Keine Persistenz, kein Netzwerktransport und kein MCP-Tool in diesem Inkrement.

### Sicherheitsgrenze

Der öffentliche Einstieg erhält die Sender-Session separat und konstruiert daraus
den `AiMessage`-Contract. Dadurch kann kein Client im Namen einer anderen Session
senden. Eine spätere MCP-Freigabe benötigt eine eigene Collaboration-Permission;
`AiPermission.Read` wird dafür nicht stillschweigend wiederverwendet.

### Speicherverhalten

Jede Inbox besitzt ein konfigurierbares Limit (Standard 500). Bei Überschreitung
werden zuerst die ältesten bestätigten, danach die ältesten übrigen Nachrichten
entfernt. Beim Entfernen abgelaufener Sessions können verwaiste Inboxen bereinigt werden.

## Nicht Bestandteil von 8.1

- persistente Nachrichten oder Zustellung nach einem Neustart
- externe Broker, WebSockets oder MCP-Tools
- automatische Task-Zuweisung
- Modellwahl, Tokenbudgets oder Kostenoptimierung
- Mensch-zu-KI-UI; dafür wird später ein eigener Human-Endpunkt registriert

## Implementierungsstand 8.1

- `AAIA.Air.Messaging.AiMessageBus` ist in `AiRuntimeService.Messages` integriert.
- Direkte Zustellung, Broadcast, Inbox-Limit, Acknowledge und Session-Purge sind umgesetzt.
- `AiMessageSafetyPolicy` blockiert offensichtliche Schlüssel und Tokens.
- Runtime-Events: `MessageSent`, `MessageDelivered`, `MessageAcknowledged`.
- Elf Messaging-Testfälle einschließlich paralleler Zustellung sind grün.

## Architekturregeln

- Contracts enthalten weiterhin nur öffentliche Typen.
- Manager und Zustandslogik liegen in `AAIA.Air`.
- Adapter bleiben in `AAIA.Air.Mcp` oder der jeweiligen Host-Anwendung.
- Vendor-Namen beeinflussen weder Routing noch Priorität.

## 8.2 — Execution Queue und Scheduler

### Zustandsmodell

`Queued → Leased → Running → Completed | Failed | Cancelled`

- Eine Lease reserviert einen Task zeitlich begrenzt für genau eine aktive Session.
- Läuft eine Lease vor dem Start ab, wird der Task bis `MaxAttempts` erneut eingereiht.
- Laufende Ausführungen werden niemals automatisch doppelt gestartet.
- Cancellation wird über einen scheduler-eigenen Token an den bestehenden
  `AiTaskManager` weitergereicht.

### Auswahl und Fairness

- Prioritäten: Low, Normal, High, Critical.
- FIFO innerhalb derselben effektiven Priorität.
- Aging hebt wartende Einträge schrittweise an, damit niedrige Prioritäten nicht verhungern.
- Rollen und Client-Capabilities sind harte Filter.
- Pro Session ist höchstens eine Lease oder Ausführung aktiv.
- Unter gleich geeigneten Sessions wird die am längsten nicht zugewiesene Session gewählt.

### Sicherheitsgrenze

Der Scheduler führt keine Tool-Handler direkt aus. Er delegiert ausschließlich an
`AiTaskManager.RunAsync`; dadurch laufen alle Schritte weiterhin über
`AiRuntimeService.InvokeToolAsync` mit Session-, Capability-, Permission-, Lock-,
Approval-, Audit- und Event-Prüfung.

### Akzeptanzkriterien

- Thread-sichere Enqueue-, Lease-, Run-, Cancel- und Recovery-Operationen.
- Kein Claim durch inaktive oder ungeeignete Sessions.
- Abgelaufene Leases werden deterministisch requeued oder endgültig fehlgeschlagen.
- Cancellation hinterlässt weder Task noch Queue-Eintrag in `InProgress`.
- Keine Modell-/Vendor-Sonderbehandlung.
- Kein MCP- oder UI-Zugriff in diesem Inkrement.

### Implementierungsstand 8.2

- `AAIA.Air.Scheduling.AiExecutionScheduler` ist als `AiRuntimeService.Scheduler` integriert.
- Queue, Priorität, FIFO+Aging, Not-before, Rollen-/Capability-Filter und faire Session-Auswahl sind umgesetzt.
- Leases werden nach Ablauf requeued und nach `MaxAttempts` endgültig fehlgeschlagen.
- Laufende Tasks unterstützen Cancellation; Task und Execution verlassen dabei zuverlässig `InProgress`/`Running`.
- Unerwartete Executor-Fehler setzen Task-Schritt, Task und Execution konsistent auf `Failed`.
- Zehn Scheduler-Tests einschließlich paralleler Assignment-Anfragen sind grün.

## 8.3 — Resource Manager

Die vollständige Spezifikation liegt in
[`phase-8.3-resource-manager-spec.md`](phase-8.3-resource-manager-spec.md).

Festgelegt sind Ressourcenprofile, Kapazitätsvektor, Kostenbudgets, Lasttelemetrie,
harte Auswahlgrenzen, deterministisches Ranking, Reservationszustände, erlaubte und
verbotene Entscheidungen sowie 26 Pflicht-Tests. Die interne AIR-Implementierung ist
abgeschlossen; MCP, UI und Host-Adapter bleiben Phase 8.4.

## 8.4 — Adapter, MCP und UI

Die vollständige Spezifikation liegt in
[`phase-8.4-adapter-mcp-ui-spec.md`](phase-8.4-adapter-mcp-ui-spec.md).

Festgelegt sind separate Permissions für Collaboration, Scheduling und lokale
Ressourcenverwaltung, eine standardmäßig geschlossene MCP-Oberfläche, Session- und
Owner-Isolation, die Trennung zwischen Transportadapter und AIR-Kern sowie explizit
bestätigte UI-Aktionen. Resource-Mutationen, Telemetrie und Reservationssteuerung
werden nicht über MCP freigegeben. Permissions, abgesicherte MCP-Werkzeuge,
Host-Telemetrie, read-only UI und lokal bestätigte/auditierte Admin-Aktionen sind
umgesetzt. Die technische Abnahme und der Merge des Implementierungs-PRs stehen aus.
