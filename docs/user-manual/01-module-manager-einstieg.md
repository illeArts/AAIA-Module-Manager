# Module Manager Einstieg

> Zielgruppe: ETWs und neue Anwender des Module Managers  
> Geprüfter Stand: 2026-06-25  
> Status: migriert aus vorhandener Hilfe, gegen aktuelle Doku-Wahrheitsregel geprüft

Der AAIA Module Manager ist das lokale Werkzeug zum Erstellen, Prüfen, Paketieren,
Signieren und Vorbereiten von AAIA-Erweiterungen. Er ersetzt keine serverseitige
Marketplace-Prüfung und setzt keine serverseitigen Trust-Stufen.

## Was der Module Manager leisten soll

| Funktion | Zweck |
|---|---|
| Projekt erstellen | Grundstruktur für Modul oder Plugin anlegen |
| Validierung | Manifest, Struktur und offensichtliche Risiken prüfen |
| Build | Quellcode kompilieren |
| Paketierung | `.aaiaext`-Paket vorbereiten |
| Paketprüfung | Inhalt vor Signatur und Upload prüfen |
| Signatur | lokale ETW-Signatur erstellen und prüfen |
| Marketplace-Upload | Paket an den Marketplace zur serverseitigen Prüfung übergeben |
| KI-/Connector-Hilfe | sichere Kontexte und Vorschläge für Entwicklungsschritte vorbereiten |

## Lokale und serverseitige Grenzen

- Lokal kann ein Paket vorbereitet, signiert und geprüft werden.
- `EtwLocalVerified` bedeutet lokale Signaturprüfung, nicht Marketplace-Freigabe.
- `MarketplaceVerified` und `MarketplacePublished` werden ausschließlich serverseitig gesetzt.
- Eine lokale Validierung ersetzt keine AAIAS- oder Marketplace-Prüfung.

## Wer braucht den Module Manager?

Primär ETWs und Entwickler. Normale Käufer oder Endanwender installieren Module über die dafür
vorgesehenen AAIAS-/Marketplace-Pfade und benötigen den Module Manager nicht für den Alltag.

## Weiterführende Seiten

- [Projekt erstellen](02-projekt-erstellen.md)
- [Validierung, Build und Paketierung](03-validierung-build-paketierung.md)
- [Rollen und Rechte](04-rollen-und-rechte.md)
- [Sicherheit und Laufzeitstatus](10-sicherheit-und-laufzeitstatus.md)
