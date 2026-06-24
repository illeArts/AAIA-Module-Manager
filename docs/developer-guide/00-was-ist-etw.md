# Was ist ein ETW?

> Zielgruppe: Entwickler von AAIA-Erweiterungen

ETW bezeichnet im AAIA-Projekt die Entwicklerrolle, die Module oder Plugins erstellt,
prüft und als Release vorbereitet. Die ausgeschriebene Langform der Abkürzung ist im
aktuellen Repository noch nicht verbindlich belegt und wird deshalb nicht erfunden.

## Verantwortungen

- eine eindeutige Erweiterungsidentität und ein gültiges Manifest pflegen,
- nur notwendige Fähigkeiten und Permissions anfordern,
- Build-, Validierungs- und Sicherheitstests ausführen,
- Release-Inhalt vor der Signatur finalisieren,
- private Schlüssel ausschließlich geschützt und lokal behandeln,
- Marketplace-Status nicht lokal setzen oder simulieren.

## Vertrauenskette

`EtwLocalSigned` bestätigt die Erstellung einer lokalen Signatur. Erst
`EtwLocalVerified` bestätigt deren lokale Prüfung. `MarketplaceVerified` und
`MarketplacePublished` sind ausschließlich serverseitige Marketplace-Stufen.

## Nächster Einstieg

Vor einer Entwicklung müssen Modultyp, Ziel (`AAIAS`, `AAIAC` oder Hybrid), Manifest und
Teststrategie feststehen. Details folgen in den geplanten Modul-, SDK- und Release-Kapiteln.
