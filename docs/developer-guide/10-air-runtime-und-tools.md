# AIR Runtime und Tools entwickeln

> Zielgruppe: ETWs, Integrationsentwickler und Runtime-Maintainer  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklungsleitlinie für implementierte Phase-10-Grenzen

AIR ist app-neutral. Der Module Manager, AAIAS oder andere Hosts nutzen AIR über Contracts,
Host-Interfaces und Adapter. AIR-Code darf keine konkrete App voraussetzen.

## Grundregeln für Tool- und Runtime-Entwicklung

- Mutationen müssen durable sein oder bewusst read-only bleiben.
- Externe Seiteneffekte benötigen eine stabile Operation-ID.
- Wiederholte Recovery darf keine externe Aktion doppelt ausführen.
- Runtime-Start, Recovery und Shutdown laufen über einen Lifecycle, nicht über UI-Nebenpfade.
- Ein Host darf Tools erst verwenden, wenn die Readiness gültig ist.
- Fehlende Protector-Schlüssel oder falscher Kontext sind Fehler, keine Aufforderung zum
  automatischen Neuanlegen produktiver Zustände.

## Persistenzmodell

Phase 10 migriert den produktiven Writer kontrolliert von Phase-9-Checkpoints auf typisierte
Delta-Ereignisse. Der neue Pfad kann alte Checkpoints lesen, Phase-10-Snapshots erzeugen und
neue Deltas verifizieren. Rollback auf den Phase-9-Writer bleibt über einen Schalter möglich.

Für Entwickler folgt daraus:

1. Neue Runtime-Mutationen müssen als typisierte Ereignisse beschreibbar sein.
2. Ereignisse müssen deterministisch auf Snapshots anwendbar sein.
3. Tests müssen Crash, Neustart, gemischtes Recovery und Sequenzlücken abdecken.
4. Diagnostik darf Betriebsstatus zeigen, aber keine Secrets ausgeben.

## Host-Integration

Hosts integrieren AIR über öffentliche Contracts und app-spezifische Adapter. UI-Elemente dürfen
Runtime-Bereitschaft anzeigen, aber keine Sicherheitsentscheidung umgehen. Wenn eine Readiness-
Lease abläuft oder `state_readiness_expired` gemeldet wird, muss der Host die betroffene Mutation
ablehnen und den Benutzer auf Recovery oder Neustart verweisen.

## Relevante Quellen

- [AIR-Plattform-Split](../air/air-platform-split.md)
- [Phase 10 Production Hardening](../phase-10-production-hardening-spec.md)
- [AIR-State-Architektur](../architecture/air-runtime-state.md)
- [Runtime Durability](runtime-durability.md)
- [Runtime-Betrieb und Recovery](../admin-guide/10-runtime-betrieb-und-recovery.md)
