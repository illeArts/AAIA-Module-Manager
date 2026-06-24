# AAIAS einrichten

> Zielgruppe: Administratoren
> Status: sicherer Vorbereitungsrahmen, keine vollständige Installationsanleitung

Eine belastbare Schritt-für-Schritt-Installation wird erst veröffentlicht, wenn unterstützte
AAIAS-Versionen, Plattformen, Paketquelle und Updatekanal verbindlich feststehen. Bis dahin
gelten folgende Mindestregeln.

## Vor der Inbetriebnahme

1. Unterstützte Betriebssystem- und Runtime-Version bestätigen.
2. Installationspaket nur aus einer freigegebenen Quelle beziehen und prüfen.
3. Dienstkonto und Datenverzeichnisse nach dem Least-Privilege-Prinzip planen.
4. Netzwerkfreigaben auf tatsächlich benötigte Schnittstellen begrenzen.
5. Backup-, Recovery- und Updateverfahren vor produktiver Nutzung testen.
6. Zugangsdaten über die vorgesehene Secret-Ablage verwalten, nie in Dokumentation oder Skriptbeispielen.

## Verbindung mit dem Module Manager

Der Module Manager benötigt eine explizit konfigurierte AAIAS-URL und authentisierte
Verbindung. Test- und Developer-Funktionen gehören nicht ungeprüft in produktive Umgebungen.
AAIAS validiert übertragene Erweiterungspakete selbst; der Module Manager ist kein Ersatz für
serverseitige Hash-, Manifest-, Signatur- oder Berechtigungsprüfungen.

## Noch zu dokumentieren

- unterstützte Installationsvarianten und Plattformen,
- Rollenmodell und initiale Administratorbereitstellung,
- Zertifikate, Reverse Proxy und Netzwerkgrenzen,
- Persistenz, Backup, Recovery, Logs, Audit und Updates.
