# Rollen, Rechte und Verantwortung

> Zielgruppe: ETWs und Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerhandbuch-Ergänzung für Phase 11.5.2/11.5.3

Entwickler arbeiten im AAIA-System nicht isoliert. Jede Änderung berührt Rollen, Rechte,
Trust-Stufen und spätere Betreiberentscheidungen. Deshalb muss ein ETW wissen, welche
Entscheidungen lokal getroffen werden dürfen und welche ausschließlich Server-, Marketplace-
oder Admin-Aufgaben sind.

## Entwicklerrolle

ETW bezeichnet die offizielle Entwickler-/Erstellerrolle im AAIA-Erweiterungs- und
Marketplace-Prozess. Die ausgeschriebene Langform ist im aktuellen Repository nicht
verbindlich belegt und wird nicht erfunden.

Ein ETW darf:

- Module und Plugins erstellen,
- Manifest und Permissions pflegen,
- lokale Validierung, Build, Tests und Paketprüfung ausführen,
- lokale ETW-Signatur erstellen und prüfen,
- Pakete zum Marketplace-Upload vorbereiten.

Ein ETW darf nicht:

- `MarketplaceVerified` oder `MarketplacePublished` lokal setzen,
- fremde Module verändern,
- Admin- oder Betreiberentscheidungen simulieren,
- Nutzer- oder Serverrechte durch Manifest-Tricks erweitern,
- Sicherheitsprüfungen für einen schnellen Release umgehen.

## Rollen im Release-Pfad

| Rolle | Verantwortet | Nicht verantwortlich für |
|---|---|---|
| ETW | Code, Manifest, lokale Signatur, Paketinhalt | Marketplace-Freigabe |
| Administrator | Betrieb, Installation, Recovery, Audit | private ETW-Schlüssel |
| Marketplace | serverseitige Prüfung, Freigabe, Sperrung | lokale Entwicklung |
| AAIAS/AAIAC | Zielhostprüfung und Ausführung | lokale Trust-Behauptungen |
| Support | Analyse anhand redigierter Daten | Zugriff auf Secrets |

## Permission-Verantwortung

Jede Permission im Manifest ist eine öffentliche Sicherheitsbehauptung. Wenn ein Modul
`filesystem.write`, `shell.exec` oder Datenbankrechte anfordert, muss der Zweck fachlich
begründet und im Code nachvollziehbar sein.

Minimalprinzip:

1. nur benötigte Rechte deklarieren,
2. Ziele so eng wie möglich beschreiben,
3. breite Rechte vermeiden,
4. keine versteckten Fähigkeiten nutzen,
5. Änderungen an Permissions als Release-relevant behandeln.

## Verweise

- [Manifest und Permissions](04-manifest-und-permissions.md)
- [Signatur und Trust-Level](06-signatur-und-trust-level.md)
- [Rollen und Rechte im Benutzerhandbuch](../user-manual/04-rollen-und-rechte.md)
