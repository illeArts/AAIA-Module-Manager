# Updates und Release-Betrieb

> Zielgruppe: Betreiber und Release-Verantwortliche  
> Geprüfter Stand: 2026-06-25  
> Status: sicherer Update-Rahmen; konkrete Paketkanäle müssen releasebezogen bestätigt werden

Updates dürfen Trust-, Signatur-, Protector- und Recovery-Grenzen nicht still verändern. Jede
produktive Aktualisierung braucht einen geprüften Rückweg.

## Vor einem Update

1. Release Notes und betroffene Komponenten prüfen.
2. Backup erstellen.
3. Recovery- und Rollback-Pfad prüfen.
4. Protector-/Secret-Store-Verfügbarkeit auf Zielplattform bestätigen.
5. Kompatibilität mit AAIAS, AAIAC, Marketplace und AIR-Konfiguration prüfen.

## Während des Updates

- Dienste kontrolliert stoppen.
- Keine State-Dateien manuell verschieben.
- Keine alten Checkpoints löschen.
- Aktivierungs- und Migrationsschalter dokumentieren.
- Nach dem Start Readiness und Diagnostik prüfen.

## Nach dem Update

- Smoke-Test ausführen.
- Logs auf Protector-, Backpressure- und Recovery-Fehler prüfen.
- Marketplace- und Signaturstatus nicht lokal überschreiben.
- Ergebnis dokumentieren.

## Rollback

Rollback ist zulässig, wenn der neue Stand nicht sicher betrieben werden kann. Es muss
dokumentiert werden:

- Ausgangsversion,
- Zielversion,
- Grund,
- gesicherter Zustand,
- betroffene Daten,
- Ergebnis nach Neustart und Recovery.
