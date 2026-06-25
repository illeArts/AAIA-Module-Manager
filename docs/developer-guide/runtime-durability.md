# Runtime Durability

> Zielgruppe: AIR-Entwickler, Host-Entwickler und technische Prüfer  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerdokumentation zu implementierten Phase-10-Durability-Regeln

Runtime Durability bedeutet: Eine AIR-Mutation wird so ausgeführt, dokumentiert und
wiederhergestellt, dass Crash, Neustart und Recovery keinen widersprüchlichen Zustand erzeugen.

## Wann ist eine Mutation durable?

Eine Mutation gilt nur dann als durable, wenn sie:

1. einen eindeutigen fachlichen Zweck hat,
2. vor externen Seiteneffekten mit einer Operation-ID abgesichert wird,
3. als typisiertes Delta-Ereignis rekonstruierbar ist,
4. deterministisch auf einen Snapshot angewendet werden kann,
5. nach Crash oder Neustart nicht doppelt ausgeführt wird,
6. bei fehlender Readiness kontrolliert abgelehnt wird.

Read-only-Operationen dürfen bewusst nicht durable sein. Sobald eine Operation lokalen State,
externe Systeme oder Tool-Ausgaben mit Seiteneffekt verändert, braucht sie Durability-Regeln.

## Operation-ID

Die Operation-ID ist der Idempotenzanker. Sie darf nicht aus einem flüchtigen UI-Zustand
abgeleitet werden und darf bei Retry nicht wechseln. Eine Recovery darf anhand der Operation-ID
erkennen, ob ein externer Seiteneffekt bereits berücksichtigt wurde.

Unzulässig:

- neue Operation-ID pro Retry,
- Operation-ID aus Zeitstempel ohne fachliche Stabilität,
- manuelles Überschreiben zur Erzwingung einer Wiederholung,
- Wiederholung externer Tool-Aktionen ohne Audit.

## Delta-Ereignisse

Typisierte Delta-Ereignisse beschreiben, was sich am Runtime-State geändert hat. Sie sind kein
Debug-Log und kein Freitext-Audit. Ein Delta muss so konkret sein, dass Recovery es in derselben
Reihenfolge wieder anwenden kann.

Für neue Delta-Typen prüfen:

- Gibt es eine stabile Sequenz?
- Ist das Ereignis versionierbar?
- Kann ein alter Snapshot plus neue Deltas gelesen werden?
- Ist das Ereignis ohne Secrets diagnostizierbar?
- Gibt es Tests für Crash, Neustart und gemischtes Recovery?

## Snapshot und Recovery

Snapshots beschleunigen Recovery, ersetzen aber nicht die Delta-Kette. Ein gültiger Recovery-Pfad
darf:

- Phase-9-Checkpoints lesen,
- Phase-10-Snapshots verifizieren,
- neue Delta-Ereignisse anwenden,
- Sequenzlücken erkennen,
- bei beschädigtem oder unsicherem Zustand fail-closed stoppen.

Er darf nicht:

- fehlende Deltas still überspringen,
- alte Checkpoints ungeprüft löschen,
- Protector-Fehler ignorieren,
- externe Seiteneffekte wiederholen, nur weil Recovery erneut startet.

## Readiness

Hosts dürfen durable Mutationen erst ausführen, wenn die Runtime bereit ist. Wenn die
Readiness-Lease abgelaufen ist, muss die Mutation abgelehnt werden. `state_readiness_expired`
ist ein korrektes Schutzsignal, kein Fehler, der durch Umgehen des Gates gelöst werden darf.

## Testanforderungen

Neue durable Mutationen brauchen mindestens Tests für:

- normale Ausführung,
- Wiederholung mit gleicher Operation-ID,
- Crash vor und nach dem Persistieren,
- Neustart mit Snapshot plus Deltas,
- fehlende oder falsche Protector-Schlüssel,
- Backpressure oder Writer-Timeout,
- Shutdown während ausstehender Mutation.

## Relevante Quellen

- [AIR-State-Architektur](../architecture/air-runtime-state.md)
- [Runtime-Betrieb und Recovery](../admin-guide/10-runtime-betrieb-und-recovery.md)
- [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md)
- [Phase 10 Production Hardening](../phase-10-production-hardening-spec.md)
