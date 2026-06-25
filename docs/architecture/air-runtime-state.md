# AIR-State-Architektur

> Zielgruppe: Maintainer, technische Prüfer und KI-Handoff-Systeme  
> Geprüfter Stand: 2026-06-25  
> Status: Architekturzusammenfassung zu Phase 9 und Phase 10

Der AIR Runtime-State ist app-neutral. Er gehört zur AIR-Schicht und wird von Hosts wie dem
Module Manager über Contracts, Adapter und Lifecycle-Gates genutzt. Die Runtime kennt keine
konkrete App und darf keine UI- oder Servergrenze direkt voraussetzen.

## Komponenten

| Komponente | Aufgabe | Grenze |
|---|---|---|
| AIR Lifecycle | Start, Recovery, Readiness, Shutdown | kennt keine konkrete App |
| Readiness Gate | verhindert Mutationen vor bestätigter Bereitschaft | ist kein UI-Schalter |
| Durable Mutation Coordinator | koordiniert dauerhafte Mutationen und Operation-IDs | führt keine unsicheren Wiederholungen aus |
| Persistence Coordinator | liest Checkpoints, schreibt Snapshots und Deltas | entscheidet nicht über fachliche Berechtigungen |
| Protector | schützt lokalen State plattformabhängig | ersetzt kein Backup und keinen Secret-Store-Betrieb |
| Host Adapter | bindet AIR an Module Manager oder andere Apps an | darf AIR-Grenzen nicht umgehen |

## Datenfluss

```text
Host / UI / Server
        |
        v
AIR Lifecycle ---- Readiness Gate
        |
        v
Durable Mutation Coordinator
        |
        v
Persistence Coordinator
        |
        +--> Phase-9-Checkpoint lesen
        +--> Phase-10-Snapshot prüfen
        +--> typisierte Delta-Ereignisse schreiben
        +--> geschützter lokaler State
```

## Persistenzprinzip

Phase 9 führte durable Runtime-State-Grundlagen ein. Phase 10 härtet den produktiven Betrieb:

- kontrollierte Writer-Migration,
- gemischtes Recovery aus altem Checkpoint und neuen Deltas,
- Snapshot-Verifikation,
- Aktivierungs- und Rollback-Schalter,
- native Protector-Pfade,
- Readiness-Lease vor Mutationen,
- geordneter Shutdown,
- Backpressure und Crash-/Restart-Konformität.

## Migration

Die Migration ist absichtlich rückwärtskompatibel:

1. Ein vorhandener Phase-9-Checkpoint bleibt lesbar.
2. Der neue Pfad kann ein Backup und einen Phase-10-Snapshot erzeugen.
3. Neue Änderungen werden als typisierte Delta-Ereignisse geschrieben.
4. Recovery kann alten Checkpoint und neue Deltas gemeinsam auswerten.
5. Rollback auf den Phase-9-Writer bleibt ein bewusster Betriebsmechanismus.

## Sicherheits- und Trust-Regeln

- Recovery darf keine Sicherheitsgrenze überspringen.
- Fehlende Schlüssel oder falscher Kontext stoppen fail-closed.
- Operation-IDs sichern externe Seiteneffekte gegen doppelte Ausführung.
- Diagnostik darf Zustände und Reason-Codes zeigen, aber keine Secrets.
- Rollback ist ein Betriebsmechanismus, kein stiller Datenverlustpfad.
- Marketplace-, Signatur- und Trust-Stufen bleiben außerhalb des lokalen Runtime-State.

## Belege

- [Phase 9 Durable Runtime State](../phase-9-durable-runtime-state-spec.md)
- [Phase 10 Production Hardening](../phase-10-production-hardening-spec.md)
- [Phase 10.1.3 Abschluss](../phases/phase-10-1-3-final.md)
- [Phase 10.2 Abschluss](../phases/phase-10-2-final.md)
- [Phase 10.3 Abschluss](../phases/phase-10-3-final.md)
- [Phase 10.4 Abschluss](../phases/phase-10-4-final.md)
