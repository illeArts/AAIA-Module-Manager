# Runtime-Betrieb und Recovery

> Zielgruppe: Betreiber und Administratoren  
> Geprüfter Stand: 2026-06-25  
> Status: Betriebswissen aus Phase 10, keine vollständige Deployment-Anleitung

Phase 10 macht den AIR-State-Betrieb robuster: alte Checkpoints bleiben lesbar, neue
Delta-Ereignisse werden typisiert geschrieben, Snapshots werden verifiziert und Recovery ist
auf Crash-/Neustart-Szenarien ausgelegt.

## Aktivierung

Die Persistenz bleibt opt-in. Produktive Aktivierung benötigt mindestens:

- bewusste Aktivierung der AIR-Persistenz,
- bewusste Aktivierung des typisierten Delta-Writers,
- getesteten Recovery-Pfad,
- geprüfte Protector-Verfügbarkeit auf der Zielplattform,
- dokumentierten Rollback-Pfad.

Ohne diese Voraussetzungen darf der produktive Betrieb nicht als abgeschlossen gelten.

## Migration und Rollback

Beim Upgrade wird der alte Phase-9-Checkpoint nicht still verworfen. Der neue Pfad kann ihn lesen,
ein Backup anlegen, einen Phase-10-Snapshot erzeugen und neue Deltas ergänzen. Wenn der neue
Writer deaktiviert werden muss, bleibt der Rollback-Schalter auf den Phase-9-Checkpoint-Writer
vorgesehen.

Rollback ist eine Betriebsentscheidung. Vorher sichern:

1. aktuellen Zustand,
2. relevante redigierte Diagnostik,
3. Version und Konfiguration,
4. Zeitpunkt und Grund der Maßnahme.

## Native Protectoren

Der lokale State wird plattformabhängig geschützt:

| Plattform | Schutzpfad | Betriebsregel |
|---|---|---|
| Windows | DPAPI-kompatibler Pfad plus native Envelope-Version | alte DPAPI-v1-Daten bleiben lesbar |
| macOS | Keychain über das Betriebssystem | fehlender Schlüssel führt fail-closed |
| Linux | Secret Service über aktive Benutzer-Session | ohne geeignete Secret-Service-/DBus-Sitzung fail-closed |

Es gibt keinen dokumentierten unsicheren Schlüsseldatei-Fallback für produktive Nutzung.

## Backpressure, Shutdown und Recovery

- Backpressure signalisiert, dass der Writer nicht rechtzeitig verfügbar ist.
- Geordneter Shutdown versucht Snapshot und Flush abzuschließen.
- `state_shutdown_incomplete` bedeutet, dass der Shutdown nicht vollständig bestätigt wurde.
- Nach Crash oder Neustart muss Recovery Sequenzen ohne Lücken rekonstruieren oder kontrolliert
  stoppen.

## Externe Tool-Seiteneffekte

Externe Tool-Aktionen müssen idempotent abgesichert sein. Operation-IDs verhindern, dass eine
Mutation bei Recovery versehentlich doppelt ausgeführt wird. Manuelle Korrekturen brauchen
Audit, Begründung und lokalen Owner-/Admin-Kontext.

## Fehlerhilfe

Siehe [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md).
