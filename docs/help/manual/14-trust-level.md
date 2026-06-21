# Trust-Level-Modell

## Hierarchie

```
Unsigned
  └─► LocalHashPrepared   (Phase 4.0 — SHA256-Hashes berechnet)
        └─► EtwLocalSigned      (Phase 4.1 — RSA-Signatur erstellt)
              └─► EtwLocalVerified    (Phase 4.2 — Signatur lokal geprüft)
                    └─► MarketplaceVerified   (Phase 5.x — Server-seitig verifiziert)
                          └─► MarketplacePublished  (Phase 6.x — veröffentlicht)
Blocked  (Sonderfall — dauerhaft gesperrt)
```

## Regeln

- `EtwLocalVerified` ist das Minimum für den Marketplace-Upload.
- `MarketplaceVerified` darf **nie** lokal gesetzt werden — nur der Marketplace-Server setzt diesen Status.
- `Blocked` kann vom Marketplace gesetzt werden und sperrt das Modul dauerhaft.
- Der Trust-Level wird im Rang verglichen (`TrustLevels.IsAtLeast`). Rang -1 = Blocked, 0 = Unsigned.

## Implementierung

```csharp
// TrustLevelDefinitions.cs
public static class TrustLevels {
    public const string EtwLocalVerified = "EtwLocalVerified";
    public static bool IsAtLeast(string current, string required)
        => Rank(current) >= Rank(required);
}
```

## Marketplace-Vertrauen

Der Marketplace verifiziert **immer unabhängig** — er vertraut nicht dem, was `signature-info.json` behauptet.
Er prüft:
1. Öffentlicher Key aus dem Fingerprint-Register
2. RSA-Signatur über den kanonischen Payload
3. Alle Dateihashes

`marketplaceReady` bleibt lokal immer `false`, bis der Marketplace selbst `MarketplaceVerified` setzt.
