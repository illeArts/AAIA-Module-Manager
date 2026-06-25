# Runtime-State und AIR-Fehler

> Zielgruppe: Anwender, Entwickler und Administratoren  
> Geprüfter Stand: 2026-06-25  
> Status: strukturierter Fehlerleitfaden für Phase-10-Betrieb

AIR- und Runtime-State-Fehler sollen kontrolliert sichtbar werden. Sicherheitsprüfungen,
Protectoren oder Recovery-Gates dürfen zur Fehlerbehebung nicht deaktiviert werden.

## Mindestinformationen

Sichere vor einer Analyse:

- Produkt und Version,
- betroffene Komponente,
- exakter Fehlercode oder Reason-Code,
- Zeitpunkt,
- redigierter Logausschnitt,
- letzte Aktion vor dem Fehler.

Nicht sammeln oder weitergeben: Tokens, Passwörter, private Schlüssel, vollständige
Konfigurationen, echte Servernamen oder private Benutzerpfade.

## Häufige Reason-Codes

| Reason-Code | Bedeutung | Sicherer nächster Schritt |
|---|---|---|
| `Backpressure` | Writer war nicht rechtzeitig verfügbar | Last reduzieren, Diagnose sichern, Runtime geordnet neu starten |
| `state_shutdown_incomplete` | Shutdown konnte nicht vollständig bestätigt werden | Neustart mit Recovery durchführen, keine Dateien manuell verändern |
| `state_readiness_expired` | Host versucht Mutation ohne gültige Readiness | Host neu initialisieren und Recovery abschließen lassen |
| `ProtectorKeyMissing` | benötigter lokaler Schutzschlüssel fehlt | Plattform-Secret-Store prüfen; keinen unsicheren Ersatzschlüssel anlegen |
| `ProtectorRotationFailed` | Schlüsselrotation konnte nicht abgeschlossen werden | Zustand sichern, Rotation stoppen, Admin-/Owner-Pfad verwenden |

## Entscheidungsbaum

```text
Fehler betrifft AIR oder Runtime-State?
        |
        +-- Nein --> themenspezifische Hilfe verwenden
        |
        +-- Ja
             |
             +-- Protector-/Schlüsselfehler?
             |       |
             |       +-- Plattform-Secret-Store prüfen, fail-closed beibehalten
             |
             +-- Start-/Recovery-Fehler?
             |       |
             |       +-- Zustand sichern, Recovery erneut kontrolliert starten
             |
             +-- Writer-/Backpressure-Fehler?
             |       |
             |       +-- Last reduzieren, keine manuelle Dateikorrektur
             |
             +-- Shutdown unvollständig?
                     |
                     +-- Neustart mit Recovery; Ergebnis dokumentieren
```

## Upgrade von Phase 9 auf Phase 10

Wenn ein alter Checkpoint vorhanden ist, darf er nicht gelöscht werden, nur weil neue Deltas
existieren. Der Phase-10-Pfad ist dafür ausgelegt, alte Checkpoints zu lesen und neue Snapshots
plus Deltas kontrolliert zu verwenden.

Bei Migrationsproblemen:

1. Anwendung stoppen.
2. Zustand sichern.
3. Version und Aktivierungsschalter erfassen.
4. Recovery erneut kontrolliert starten.
5. Falls nötig Rollback auf den Phase-9-Writer nur als bewusste Admin-Maßnahme verwenden.

## Plattformhinweise

- Windows: alte DPAPI-v1-Daten bleiben lesbar.
- macOS: Keychain muss verfügbar sein.
- Linux: Secret Service benötigt eine aktive Benutzer-Session.

Wenn der Secret Store fehlt, ist fail-closed korrektes Verhalten.

## Was nicht getan werden darf

- keine Protector-Prüfung deaktivieren,
- keine State-Dateien manuell zusammenkopieren,
- keine alten Checkpoints löschen, bevor Backup und Recovery geprüft sind,
- keine Operation-IDs neu vergeben, um Wiederholungen zu erzwingen,
- keine Marketplace-, Signatur- oder Trust-Stufe lokal „reparieren“.

## Eskalation

Eine Eskalation an Entwickler oder Betreiber braucht:

1. Reason-Code,
2. Produkt- und Commit-/Release-Stand,
3. aktivierte AIR-Persistenzschalter,
4. Plattform und Secret-Store-Verfügbarkeit,
5. redigierten Recovery-/Shutdown-Verlauf,
6. Angabe, ob ein Phase-9-Checkpoint und Phase-10-Deltas gemeinsam vorhanden sind.
