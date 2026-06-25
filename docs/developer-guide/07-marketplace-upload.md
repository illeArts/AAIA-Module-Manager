# Marketplace-Upload

> Zielgruppe: ETWs und Release-Verantwortliche  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerhandbuch-Ergänzung aus vorhandener Hilfe

Der Marketplace-Upload übergibt ein lokal vorbereitetes Paket zur serverseitigen Prüfung.
Upload bedeutet nicht Veröffentlichung.

## Upload-Voraussetzungen

Vor einem Upload müssen erfüllt sein:

- Build erfolgreich,
- Validierung ohne Blocker,
- Paketprüfung abgeschlossen,
- lokale Signatur erstellt,
- Trust-Level mindestens `EtwLocalVerified`,
- Marketplace-Authentisierung vorhanden,
- Lizenz-/MoR-Anforderungen geklärt, falls relevant,
- keine Secrets oder privaten Schlüssel im Paket.

## Ablauf

1. Release-Artefakt finalisieren.
2. Paket signieren.
3. Signatur lokal verifizieren.
4. Upload im Marketplace-Bereich starten.
5. Serverprüfung abwarten.
6. Ergebnis prüfen und dokumentieren.

## Status nach Upload

| Status | Bedeutung |
|---|---|
| Pending Review | Marketplace prüft serverseitig |
| MarketplaceVerified | Prüfung bestanden, Veröffentlichung noch nicht zwingend aktiv |
| MarketplacePublished | öffentlich sichtbar bzw. kaufbar |
| Blocked | abgelehnt oder gesperrt |

## Häufige Blocker

- Trust-Level unter `EtwLocalVerified`,
- ungültiges oder fehlendes Marketplace-Token,
- MoR-/Lizenzvoraussetzungen nicht erfüllt,
- Manifest oder Permissions unplausibel,
- Paketinhalt nach Signatur verändert,
- verbotene Dateien oder Secrets im Paket.

## Sicherheitsgrenze

Der Module Manager darf die Upload-Bereitschaft anzeigen, aber nicht die serverseitige
Freigabe ersetzen. Entwickler dürfen serverseitige Statusfelder nicht manuell setzen.

## Verweise

- [Signatur und Trust-Level](06-signatur-und-trust-level.md)
- [Validierung, Build und Paketierung](03-validierung-build-paketierung.md)
- [Marketplace-Upload Fehlerhilfe](../help/troubleshooting/marketplace-upload-fehler.md)
