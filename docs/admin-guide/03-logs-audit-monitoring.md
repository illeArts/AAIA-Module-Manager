# Logs, Audit und Monitoring

> Zielgruppe: Betreiber, Administratoren und Support  
> Geprüfter Stand: 2026-06-25  
> Status: sicherer Betriebsrahmen

Logs und Auditdaten sollen Fehler reproduzierbar machen, ohne Secrets offenzulegen. Monitoring
zeigt Zustand und Trends, ersetzt aber keine Sicherheitsentscheidung.

## Was protokolliert werden darf

- Zeitpunkte,
- Komponente,
- Fehler- und Reason-Codes,
- Operation-ID,
- redigierte technische Details,
- Benutzer-/Rollenbezug, soweit betrieblich erforderlich und datenschutzkonform,
- Ergebnis einer Admin-Maßnahme.

## Was nicht protokolliert oder geteilt werden darf

- Passwörter,
- Tokens,
- private Schlüssel,
- vollständige Secret-Store-Inhalte,
- echte private Benutzerpfade,
- unredigierte Konfigurationsdateien.

## Auditpflichtige Maßnahmen

- Recovery und Rollback,
- Protector-Rotation,
- Resource- oder Budgetänderungen,
- Upload-, Sperr- oder Freigabeentscheidungen,
- manuelle Admin-Eingriffe,
- Wiederholung externer Tool-Aktionen.

## Monitoring-Signale

- wiederholte `Backpressure`,
- unvollständiger Shutdown,
- fehlende Protector-Schlüssel,
- Recovery mit Sequenzproblemen,
- ablaufende Readiness-Leases,
- wiederholte Marketplace-Upload-Fehler.
