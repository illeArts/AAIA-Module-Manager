# Release-Signatur vorbereiten

> Zielgruppe: ETWs und Release-Verantwortliche  
> Geprüfter Stand: 2026-06-25  
> Status: Workflow-Seite; Detailseiten sind `06-signatur-und-trust-level.md` und `07-marketplace-upload.md`

Diese Seite beschreibt den praktischen Workflow vor einem Release. Die Trust-Level-Details
stehen in [Signatur und Trust-Level](06-signatur-und-trust-level.md), der Upload-Prozess in
[Marketplace-Upload](07-marketplace-upload.md).

## Vor dem Signieren

- Build erfolgreich,
- Tests und Validierung ausgeführt,
- Paketinhalt geprüft,
- Manifest und Version final,
- keine Secrets, privaten Schlüssel oder lokalen Pfade im Paket,
- Entwickleridentität korrekt konfiguriert.

## Nach dem Signieren

Nach der Signatur dürfen signierte Inhalte nicht still verändert werden. Bei Änderungen muss
das Paket erneut geprüft und signiert werden.

## Release-Übergang

Erst nach lokaler Signaturprüfung darf der Marketplace-Upload vorbereitet werden. Der Upload
ist eine Übergabe zur serverseitigen Prüfung, nicht die Veröffentlichung.

Mindestbedingung für den Upload ist `EtwLocalVerified`. `MarketplaceVerified` und
`MarketplacePublished` werden ausschließlich vom Marketplace gesetzt.

## Verweise

- [Validierung, Build und Paketierung](03-validierung-build-paketierung.md)
- [Signatur und Trust-Level](06-signatur-und-trust-level.md)
- [Marketplace-Upload](07-marketplace-upload.md)
- [Sicherheit und Laufzeitstatus für Entwickler](10-sicherheit-und-laufzeitstatus.md)
