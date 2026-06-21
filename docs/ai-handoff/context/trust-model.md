# Trust-Modell — KI-Kontext

Dieses Dokument wird als Teil des AI Handoffs bereitgestellt, damit die KI das Trust-Modell des AAIA Module Managers versteht.

## Trust-Level-Hierarchie

```
Unsigned (0)
  LocalHashPrepared (1)     — SHA256-Hashes aus Phase 4.0
  EtwLocalSigned (2)        — RSA-2048-Signatur (Phase 4.1)
  EtwLocalVerified (3)      — Lokal geprüft (Phase 4.2)
  MarketplaceVerified (4)   — Server-seitig verifiziert
  MarketplacePublished (5)  — Veröffentlicht
  Blocked (-1)              — Gesperrt
```

## Invarianten (nie verletzen)

- `MarketplaceVerified` wird NUR vom Server gesetzt.
- `marketplaceReady` bleibt lokal immer `false`.
- Private Key wird NIEMALS an den Marketplace oder eine KI übertragen.
- `isCryptographicallySigned: false` solange kein ETW-Schlüssel vorhanden.

## Implementierung

```csharp
// Services/Signing/TrustLevelDefinitions.cs
public static class TrustLevels {
    public const string Unsigned             = "Unsigned";
    public const string LocalHashPrepared    = "LocalHashPrepared";
    public const string EtwLocalSigned       = "EtwLocalSigned";
    public const string EtwLocalVerified     = "EtwLocalVerified";
    public const string MarketplaceVerified  = "MarketplaceVerified";
    public const string MarketplacePublished = "MarketplacePublished";
    public const string Blocked              = "Blocked";

    public static int  Rank(string level) => level switch { Unsigned => 0, LocalHashPrepared => 1, ... };
    public static bool IsAtLeast(string current, string required) => Rank(current) >= Rank(required);
}
```

## signature-info.json Felder (Phase 4.2)

```json
{
  "signatureVersion":           "etw-signature-v1",
  "signaturePhase":             "EtwLocalSigned",
  "trustLevel":                 "EtwLocalVerified",
  "isCryptographicallySigned":  true,
  "keyFingerprint":             "SHA256:XX:XX:...",
  "keyStorageMode":             "LocalFile",
  "canonicalPayload":           "etw-signature-v1\nextensionId:...\n...",
  "payloadSha256":              "...",
  "signature":                  "<base64>",
  "publicKey":                  "-----BEGIN PUBLIC KEY-----\n...",
  "marketplaceReady":           false
}
```
