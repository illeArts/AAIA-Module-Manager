# Phase 8 — AI Collaboration & Orchestration

## Ziel

Phase 8 ergänzt die AIR um herstellerneutrale Zusammenarbeit und Ausführungsplanung.
Die bestehende Sicherheitskette für Tools bleibt unverändert; Messaging, Scheduling
und Ressourcenwahl dürfen sie weder ersetzen noch umgehen.

## Inkremente

| Inkrement | Inhalt | Status |
|---|---|---|
| 8.1 | Sessiongebundener Messaging-Bus | abgeschlossen |
| 8.2 | Execution Queue und Scheduler | geplant |
| 8.3 | Resource Manager für Capabilities, Kosten- und Lastprofile | geplant |
| 8.4 | Adapter-/MCP-Oberfläche und UI | geplant, nach Permission-Entscheidung |

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
