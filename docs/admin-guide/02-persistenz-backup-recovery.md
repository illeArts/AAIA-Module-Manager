# Persistenz, Backup und Recovery

> Zielgruppe: Betreiber und Administratoren  
> Geprüfter Stand: 2026-06-25  
> Status: Betriebsrahmen zu State, Backup und Recovery

Persistenz schützt Runtime-Zustand, ersetzt aber kein Backup. Recovery ist ein kontrollierter
Pfad, kein manueller Dateiedit.

## Backup-Grundsätze

- Vor Migration, Rollback oder Reparatur Zustand sichern.
- Backups nicht mit Tokens, privaten Schlüsseln oder unredigierten Konfigurationen in Tickets
  kopieren.
- Backup-Wiederherstellung in Testumgebung oder Wartungsfenster prüfen.
- Recovery-Ergebnis dokumentieren.

## Runtime-State

Für AIR gelten zusätzlich die Phase-10-Regeln:

- Phase-9-Checkpoints bleiben lesbar.
- Phase-10-Snapshots werden verifiziert.
- Neue Änderungen werden als typisierte Deltas geschrieben.
- Operation-IDs schützen externe Seiteneffekte.
- Fehler bei Schlüsseln, Sequenzen oder Kontext führen fail-closed.

## Recovery-Reihenfolge

1. Anwendung oder Dienst kontrolliert stoppen.
2. Zustand sichern.
3. Produktversion und Aktivierungsschalter erfassen.
4. Recovery starten und Ergebnis prüfen.
5. Bei Sequenz-, Protector- oder Backpressure-Fehlern nicht manuell korrigieren.
6. Rollback nur als bewusste Admin-Maßnahme mit dokumentiertem Grund.

## Verweise

- [Runtime-Betrieb und Recovery](10-runtime-betrieb-und-recovery.md)
- [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md)
