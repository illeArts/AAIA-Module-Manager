# Rollen und Rechte

> Zielgruppe: Anwender, ETWs und Administratoren  
> Geprüfter Stand: 2026-06-25  
> Status: bereinigte Rollenübersicht; ETW-Langform bleibt offen

AAIA trennt Rollen, damit lokale Entwicklung, Kauf, Betrieb und Marketplace-Prüfung nicht
vermischt werden.

## Rollen

| Rolle | Aufgabe | Darf nicht |
|---|---|---|
| Anwender | nutzt installierte Erweiterungen | Marketplace-Freigaben setzen |
| Käufer | besitzt oder abonniert eine Erweiterung | fremde Erweiterungen ändern |
| ETW | erstellt und signiert Erweiterungen | `MarketplaceVerified` lokal setzen |
| Administrator | betreibt AAIAS und verwaltet Betriebspfade | private Schlüssel anderer ETWs verlangen |
| Marketplace | prüft und veröffentlicht Pakete serverseitig | lokale ETW-Prüfung ersetzen |

ETW bezeichnet die Entwickler-/Erstellerrolle. Die ausgeschriebene Langform ist im aktuellen
Repository nicht verbindlich belegt.

## Trust-Stufen

| Stufe | Bedeutung |
|---|---|
| `Unsigned` | nicht signiert |
| `LocalHashPrepared` | lokale Hashes vorbereitet |
| `EtwLocalSigned` | lokale ETW-Signatur erstellt |
| `EtwLocalVerified` | lokale ETW-Signatur geprüft |
| `MarketplaceVerified` | Marketplace hat serverseitig geprüft |
| `MarketplacePublished` | Marketplace hat veröffentlicht |
| `Blocked` | gesperrt |

Lokale Tools dürfen serverseitige Stufen nicht simulieren.

## Sicherheitsregel

Rollenrechte dürfen nicht über Supportanfragen, KI-Prompts oder manuelle Dateiedits erweitert
werden. Bei Unsicherheit gilt fail-closed.
