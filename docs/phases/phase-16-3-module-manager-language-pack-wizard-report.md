"H:\AAIAGitHub\Codex\AndreAIAgent\Universal AAIA\docs\phases\phase-16\phase-16-3-module-manager-language-pack-wizard-report.md"
# Phase 16.3 Module Manager Language Pack Wizard Report

Date: 2026-06-27

## Goal

Phase 16.3 adds the local Module Manager foundation for preparing ETW/Publisher language-pack drafts.

The wizard can create a local language-pack project, generate a manifest, validate translations against a registry, report DryRun issues, and build a local package draft. It does not upload, publish, approve, install, activate, or execute language packs.

## Boundary To Phase 16.1 And 16.2

Phase 16.1 defined the language-pack governance boundary, manifest model, target kinds, status model, protected glossary terms, and runtime non-goals.

Phase 16.2 defined the Translation Key Registry as the catalog of canonical source keys.

Phase 16.3 consumes those concepts locally in the Module Manager. It does not change the governance model or become a Marketplace gatekeeper.

## Boundary To Phase 16.4

Phase 16.4 remains the Marketplace Upload Inspector phase.

Phase 16.3 can produce a local draft package and validation evidence. It cannot treat that evidence as Marketplace approval, cannot bind the publisher identity to an authenticated Marketplace upload context, and cannot set Marketplace verification or publish status.

## Wizard Fields

The local wizard model supports:

- `packageId`
- `name`
- `version`
- `locale`
- `fallbackLocale`
- `targetKind`
- `targetPackageId` optional
- `minAaiaVersion`
- `publisherEtwId`
- `containsSecurityText`
- `containsLegalText`
- `containsMarketplaceText`

Supported `targetKind` values:

- `core-ui`
- `setup-first-run`
- `marketplace`
- `module`
- `documentation`
- `legal-compliance`

`publisherEtwId` is written into the local manifest for traceability, but it is not trusted as authoritative publisher truth. The authoritative binding must later come from the authenticated Marketplace/ETW upload context.

## Local Project Structure

The wizard prepares this local structure:

```text
language-pack/
  manifest.json
  translations/
    <locale>.json
  evidence/
    validation-report.json
  README.md
```

`evidence/validation-report.json` contains DryRun results and boundary flags only. It must not contain secrets, private keys, Marketplace tokens, or productive approval material.

## Manifest Generation

The generated manifest uses:

- `schemaVersion = 1.0`
- `type = language-pack`
- canonical target kind
- validated locale and fallback locale
- ETW publisher field for traceability only
- prepared `files[]` SHA-256 metadata for translation files

Productive signatures and Marketplace approval tokens remain out of scope.

## Registry Check

The local DryRun validator checks draft translations against a registry:

- unknown keys
- missing required translatable keys
- duplicate draft keys
- locked key overrides
- `translatable=false` overrides
- missing placeholders
- extra placeholders
- protected glossary terms
- security review requirements
- legal Owner/Admin review requirements
- Marketplace review requirements
- maxLength warnings

DryRun reports only. It does not install or activate anything.

## Translation Draft Format

Phase 16.3 uses an entry-based draft format:

```json
{
  "locale": "de-DE",
  "entries": [
    {
      "namespace": "core-ui",
      "key": "package.signature.invalid",
      "text": "AAIA Paket {packageId} Signatur ist ungueltig.",
      "reviewState": "draft",
      "glossaryTerms": ["AAIA"],
      "hasSecurityReview": true,
      "hasOwnerAdminReview": false,
      "hasMarketplaceReview": false
    }
  ]
}
```

This format keeps namespace, key, text, review flags, placeholders through registry metadata, and glossary terms explicit enough for later review tooling.

## Package Draft

The wizard can build a local `.aaialangdraft` ZIP containing:

- `manifest.json`
- `translations/<locale>.json`
- `evidence/validation-report.json`
- `README.md` when present

The package status is local-only:

- `draft`
- `local_validated`

Neither status means Marketplace approval.

## Tests

Added tests cover:

- Wizard inputs create a valid manifest.
- Invalid locale is blocked.
- Unknown target kind is blocked.
- Unknown registry key is detected.
- Locked key override is blocked.
- `translatable=false` key override is blocked.
- Placeholder mismatch is detected.
- Glossary hint is detected.
- Security review requirement is marked.
- Legal review requirement is marked.
- Marketplace review requirement is marked.
- maxLength warning is generated.
- Package draft contains manifest, translations, and validation report.
- Upload, publish, runtime activation, client install, and FirstRun Apply remain disabled.

## Implemented Artifacts

- `src/AAIA.ModuleManager/Services/LanguagePacks/LanguagePackWizardService.cs`
- `src/AAIA.ModuleManager.Tests/LanguagePackWizardServiceTests.cs`
- `docs/phases/phase-16-3-module-manager-language-pack-wizard-report.md`

## Explicit Non-Goals

Phase 16.3 does not add:

- Productive Marketplace upload
- WooCommerce or WordPress code
- Admin Review/Freigabe UI
- Client language-pack installation
- Client Preview UI
- FirstRun Apply
- Automatic language-pack activation
- SecureLink/VPN/mTLS changes
- Pairing/enrollment runtime changes

## Backlog

- Phase 16.4 Marketplace Upload Inspector
- Phase 16.5 Admin Review/Freigabe
- Phase 16.6 Client/FirstRun Preview
- Phase 16.7 Revoke/Rollback
- UI surface for the local wizard
- Registry file import/export
- Language-pack SDK/template scaffolding
- AI translation proposals with mandatory human review
- ICU/plural metadata
- RTL metadata
- UI text fitting checks
- Support diagnostics for locale, pack version, fallback locale, target package, and module version
