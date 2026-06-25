# Signatur und Trust-Level

> Zielgruppe: ETWs und Release-Verantwortliche  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerhandbuch-Ergänzung aus Help- und AI-Handoff-Quellen

Die lokale ETW-Signatur beweist Herkunft und Integrität eines Pakets im lokalen Release-Pfad.
Sie ist kein Ersatz für die serverseitige Marketplace-Verifikation.

## Trust-Level

```text
Unsigned
  -> LocalHashPrepared
  -> EtwLocalSigned
  -> EtwLocalVerified
  -> MarketplaceVerified
  -> MarketplacePublished

Blocked ist ein sperrender Sonderstatus.
```

## Bedeutung der Stufen

| Stufe | Bedeutung | Wer setzt sie? |
|---|---|---|
| `Unsigned` | Paket ist nicht signiert | lokaler Zustand |
| `LocalHashPrepared` | Hashes wurden vorbereitet | Module Manager |
| `EtwLocalSigned` | lokale ETW-Signatur wurde erstellt | Module Manager / ETW |
| `EtwLocalVerified` | lokale ETW-Signatur wurde geprüft | Module Manager |
| `MarketplaceVerified` | Marketplace hat serverseitig geprüft | Marketplace |
| `MarketplacePublished` | Marketplace hat veröffentlicht | Marketplace |
| `Blocked` | Paket oder Release ist gesperrt | Marketplace oder zuständiger Prüfpfad |

## Signaturinhalt

Die Signatur muss auf einem stabilen, kanonischen Payload beruhen. Typische Bestandteile sind
Extension-ID, Version, ETW-ID, Paket-Hash, Release-Info-Hash, Prüfbericht-Hash und
Signaturzeitpunkt. Nachträgliche Änderungen am signierten Inhalt machen eine erneute Prüfung
und Signatur erforderlich.

## Private-Key-Regel

Private Schlüssel:

- niemals in Git,
- niemals in KI-Prompts,
- niemals in Tickets,
- niemals in Marketplace-Uploads,
- niemals in Beispiel-Dokumentation.

Der Public Key oder Fingerprint darf für Prüfung und Zuordnung verwendet werden. Der Private
Key bleibt lokal geschützt.

## Harte Grenze

`MarketplaceVerified` darf nie aus `signature-info.json`, UI-Zustand oder lokalem Vertrauen
abgeleitet werden. Der Marketplace verifiziert unabhängig.

## Verweise

- [Release-Signatur vorbereiten](04-signatur-und-marketplace-release.md)
- [Marketplace-Upload](07-marketplace-upload.md)
- [Trust-Modell KI-Kontext](../ai-handoff/context/trust-model.md)
