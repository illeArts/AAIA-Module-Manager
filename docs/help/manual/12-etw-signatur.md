# ETW-Signatur (Phase 4.1 / 4.2)

## Was ist die ETW-Signatur?

Die ETW-Signatur ist eine **lokale RSA-2048-Entwickler-Signatur**. Sie beweist:

- Das Paket stammt von einem bestimmten ETW-Entwickler (erkennbar an der ETW-ID).
- Die Dateien wurden seit der Signierung nicht verändert.
- Der Fingerprint des Public Key kann mit dem Marketplace-Eintrag abgeglichen werden.

Sie ist **kein Ersatz für die Marketplace-Verifikation** — die Marketplace-Verifikation erfolgt serverseitig und unabhängig.

---

## Ablauf

```
[ETW-Schlüssel erzeugen]
        ↓
[ETW-Signatur erstellen] → signature-info.json (signatureVersion: "etw-signature-v1")
        ↓
[ETW-Signatur prüfen]   → TrustLevel: EtwLocalVerified
        ↓
[Marketplace-Upload]    → Marketplace prüft unabhängig
```

---

## Schlüsselspeicherung

Phase 4.1/4.2: Schlüssel liegen als PEM-Dateien in `%APPDATA%\AAIA\Keys\`.

| Datei                        | Inhalt                   |
|------------------------------|--------------------------|
| `{etwId}-private.pem`        | PKCS#8 — **GEHEIM**      |
| `{etwId}-public.pem`         | SPKI — darf geteilt werden |
| `{etwId}-key-info.json`      | Metadaten + Fingerprint  |

**Wichtig:** Den Private Key niemals in Git einchecken oder an den Marketplace hochladen.

---

## Kanonischer Payload

Die Signatur deckt folgenden Inhalt ab:

```
etw-signature-v1
extensionId:{id}
extensionVersion:{version}
developerEtwId:{etwId}
packageSha256:{hash}
releaseInfoSha256:{hash}
inspectionReportSha256:{hash}
signedAtUtc:{iso8601}
```

`release-info.json` selbst wird nach der Signierung modifiziert (trustLevel etc.) — das ist gewollt.
Sein Hash im kanonischen Payload ist ein Vor-Signatur-Schnappschuss.

---

## Fingerprint

Format: `SHA256:XX:XX:XX:...` (SHA256-Hash des DER-kodierten SPKI).

Der Fingerprint ermöglicht es dem Marketplace, den Public Key zu identifizieren, ohne den ganzen PEM-Block übertragen zu müssen.

---

## Trust-Level nach Signatur

| Schritt                | Trust-Level          |
|------------------------|----------------------|
| Nur Hash-Vorbereitung  | LocalHashPrepared    |
| ETW-Signatur erstellt  | EtwLocalSigned       |
| ETW-Signatur geprüft   | EtwLocalVerified ✓   |
| Marketplace verifiziert| MarketplaceVerified  |

`EtwLocalVerified` schaltet die Marketplace-Schaltfläche frei.
