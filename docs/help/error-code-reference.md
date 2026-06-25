# Fehlercode- und Reason-Code-Referenz

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet für AAIAM-Import und In-App-Hilfe  
> Regel: Sicherheitsprüfungen dürfen zur Fehlerbehebung nicht deaktiviert werden.

Diese Referenz sammelt Runtime-State- und Help-relevante Reason-Codes. Sie ist eine
Dokumentationsreferenz, keine Implementierung neuer Fehlercodes.

## `state_backpressure`

- **Bedeutung:** Der Runtime-State-Writer war nicht rechtzeitig verfügbar.
- **Zielgruppe:** Entwickler, Administratoren, Support.
- **Mögliche Ursache:** hohe Last, blockierter Writer, zu knappes Backpressure-Timeout.
- **Sichere Handlung:** Last reduzieren, Diagnose sichern, Runtime geordnet neu starten.
- **Eskalation:** wiederholtes Auftreten mit Version, Zeitpunkt und redigierten Logs melden.
- **Verweise:** [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md),
  [Runtime-Betrieb und Recovery](../admin-guide/10-runtime-betrieb-und-recovery.md).

## `state_shutdown_incomplete`

- **Bedeutung:** Shutdown, Snapshot oder Flush wurde nicht vollständig bestätigt.
- **Zielgruppe:** Administratoren, Entwickler.
- **Mögliche Ursache:** Timeout, Prozessabbruch, blockierte Persistenz.
- **Sichere Handlung:** Neustart mit Recovery durchführen; State-Dateien nicht manuell ändern.
- **Eskalation:** Recovery-Ergebnis und redigierte Diagnose dokumentieren.
- **Verweise:** [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md).

## `state_protector_key_missing`

- **Bedeutung:** Der benötigte lokale Schutzschlüssel ist nicht verfügbar.
- **Zielgruppe:** Administratoren, Support.
- **Mögliche Ursache:** fehlender Secret Store, falscher Benutzerkontext, beschädigte
  Plattformschlüssel.
- **Sichere Handlung:** Plattform-Secret-Store prüfen; keinen unsicheren Ersatzschlüssel
  anlegen.
- **Eskalation:** Plattform, Benutzerkontext und Secret-Store-Status redigiert melden.
- **Verweise:** [Runtime-Betrieb und Recovery](../admin-guide/10-runtime-betrieb-und-recovery.md).

## `state_protector_rotation_failed`

- **Bedeutung:** Protector-Schlüsselrotation konnte nicht abgeschlossen werden.
- **Zielgruppe:** Administratoren, Maintainer.
- **Mögliche Ursache:** fehlender Zugriff auf Secret Store, Unterbrechung während Rotation.
- **Sichere Handlung:** Zustand sichern, Rotation stoppen, Owner-/Admin-Pfad verwenden.
- **Eskalation:** keine erneute Rotation erzwingen; redigierte Diagnose bereitstellen.
- **Verweise:** [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md).

## `state_lifecycle_invalid_transition`

- **Bedeutung:** Runtime-Lifecycle wurde in einer ungültigen Reihenfolge verwendet.
- **Zielgruppe:** Entwickler, Maintainer.
- **Mögliche Ursache:** Host startet Tools vor Recovery, Stop/Start-Reihenfolge falsch,
  Readiness-Gate nicht beachtet.
- **Sichere Handlung:** Lifecycle-Reihenfolge prüfen; Mutationen vor Readiness ablehnen.
- **Eskalation:** Host-Flow, Zustand und letzte Aktion redigiert dokumentieren.
- **Verweise:** [AIR Runtime und Tools](../developer-guide/10-air-runtime-und-tools.md),
  [AIR-State-Architektur](../architecture/air-runtime-state.md).

## `state_readiness_expired`

- **Bedeutung:** Host versucht eine Mutation ohne gültige Readiness-Lease.
- **Zielgruppe:** Entwickler, Administratoren.
- **Mögliche Ursache:** abgelaufene Lease, unvollständiges Recovery, Host verwendet alten
  Runtime-Zustand.
- **Sichere Handlung:** Host neu initialisieren und Recovery abschließen lassen.
- **Eskalation:** wiederholte Fälle mit Host-Flow und Reason-Code melden.
- **Verweise:** [Runtime Durability](../developer-guide/runtime-durability.md),
  [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md).
