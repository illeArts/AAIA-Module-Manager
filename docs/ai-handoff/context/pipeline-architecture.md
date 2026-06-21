# Pipeline-Architektur — KI-Kontext

## Tech-Stack

- **Framework:** Avalonia 11, MVVM via CommunityToolkit.Mvvm
- **Patterns:** `[ObservableProperty]`, `[RelayCommand]`, `partial void OnXxxChanged`
- **Pipeline-Gate-Pattern:** boolean Flags + computed `CanXxx` Properties + `NotifyGates()` propagiert alles

## Service-Verzeichnis

```
Services/
  Signing/
    TrustLevelDefinitions.cs   — Trust-Level-Konstanten + Rank()-Vergleich
    SignatureModels.cs         — EtwKeyInfo, EtwSigningResult, EtwVerificationResult
    EtwKeyStoreService.cs      — RSA-2048-Schlüsselpaare in %APPDATA%/AAIA/Keys/
    EtwSigningService.cs       — Signiert Release, schreibt signature-info.json
    EtwSignatureVerificationService.cs — Prüft ETW-Signatur
    ReleaseSignatureService.cs — Phase 4.0: Hash-Vorbereitung
  Help/
    AiHandoffModels.cs         — DTOs für Handoff (Phase 4.5)
    AiHandoffGeneratorService.cs — Prompt-Generator (Phase 4.5)
```

## Wizard-Schritte (WizardStep Enum)

| Step              | Schritt | Inhalt                              |
|-------------------|---------|-------------------------------------|
| IdeaInput         | 0       | Idee eingeben                       |
| IdeaResult        | 1       | KI-Analyse-Ergebnis                 |
| ProjectDetails    | 2       | Projekttyp + Name + Pfad            |
| Success           | 3       | Erstellung + Build + IDE            |
| Validation        | 4       | Manifest + Kompatibilitätsprüfung   |
| PublishReadiness  | 5       | Paket + Inspektion + Release        |
| Signature         | 6       | Signatur + ETW + Marketplace        |

## Pipeline-Gates (Reihenfolge erzwungen)

```
IsProjectCreated
  → IsValidated + !HasValidationBlockers
    → IsBuilt + BuildSucceeded
      → IsPackaged
        → IsInspected + !HasInspectionBlockers
          → IsReleasePrepared
            → IsSignaturePrepared (LocalHashPrepared)
              → IsEtwSigned (EtwLocalSigned)
                → IsEtwSignatureVerified (EtwLocalVerified)
                  → CanContinueToMarketplace
```

## Schlüsseldateien

- `ViewModels/NewProjectWizardViewModel.cs` — zentrales ViewModel, alle Gates + Commands
- `Views/NewProjectWizardWindow.axaml` — komplette Wizard-UI (Step 0–6)
- `Views/NewProjectWizardWindow.axaml.cs` — Code-behind, Radio-Buttons, Step-Visibility
