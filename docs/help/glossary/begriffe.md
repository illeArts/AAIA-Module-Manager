# Glossar

## .aaiaext
ZIP-Archiv mit dem kompilierten Modul. Enthält: Binaries, `aaia-extension.json`, `package-info.json`.

## Canonical Payload
Deterministischer UTF-8-String aus den wichtigsten Signaturfeldern. Wird RSA-signiert und beim Verifizieren rekonstruiert.

## ETW-ID
Entwickler-Identifikator im Format `{nachname}.{vorname}`. Wird für Schlüsseldateinamen und als `developerEtwId` in der Signatur verwendet.

## Fingerprint
SHA256-Hash des DER-kodierten Public Keys. Format: `SHA256:XX:XX:...`. Dient zur Schlüssel-Identifikation ohne PEM-Übertragung.

## LocalHashPrepared
Trust-Level nach Phase 4.0. SHA256-Hashes aller Release-Dateien wurden berechnet und in `signature-info.json` gespeichert.

## EtwLocalSigned
Trust-Level nach Phase 4.1. RSA-2048-Signatur wurde mit dem lokalen ETW-Schlüssel erstellt.

## EtwLocalVerified
Trust-Level nach erfolgreicher lokaler Prüfung (Phase 4.2). Schaltet den Marketplace-Upload-Knopf frei.

## MarketplaceVerified
Wird ausschließlich vom Marketplace-Server gesetzt — nie lokal.

## PKCS#8
Standard für Private-Key-Dateien (`.pem`). Wird mit `RSA.ExportPkcs8PrivateKeyPem()` erzeugt.

## SPKI
SubjectPublicKeyInfo — Standard für Public-Key-Dateien. Wird mit `RSA.ExportSubjectPublicKeyInfoPem()` erzeugt.

## signature-info.json
JSON-Datei im Release-Ordner. Enthält Hash-Vorbereitung (Phase 4.0) und optionale ETW-Signatur (Phase 4.1+).

## Trust-Level
Sicherheitsstufe eines Release. Aufsteigend: Unsigned → LocalHashPrepared → EtwLocalSigned → EtwLocalVerified → MarketplaceVerified → MarketplacePublished.
