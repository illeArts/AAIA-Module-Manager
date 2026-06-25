# Manual Release Gate

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, Freigabe ausstehend  
> Scope: Website-, PDF-, In-App- und AAIAM-Ausgaben

Dieses Gate verhindert, dass Release-Candidate-Artefakte aus `docs/.release-candidate/`
automatisch veröffentlicht, importiert oder als finale Ausgabe behandelt werden.

## Zweck

Phase 11.5.10 ist die kontrollierte Freigabeschicht vor jeder Ausgabe. Das Gate prüft, ob ein
lokaler Release Candidate fachlich, redaktionell und sicherheitstechnisch bereit für einen
nachgelagerten Ausführungsschritt ist.

Eine Freigabe ist eine menschliche Entscheidung. Eine KI darf Hinweise geben, Prüfungen
vorbereiten oder Fehler melden, aber keine Freigabe setzen.

## Zu prüfende Artefakte

| Artefakt | Quelle | Prüfung |
|---|---|---|
| Website-RC | `docs/.release-candidate/website/` | Routen, Quellen, Legacy-Aliase, Status, keine Veröffentlichungsbehauptung |
| PDF-RC / PDF-Source | `docs/.release-candidate/pdf/` | Reihenfolge, Zielgruppe, Stand, Quellen, optionaler PDF-Status |
| In-App-Hilfe-RC | `docs/.release-candidate/in-app/help-contexts.json` | `contextId`, Zielgruppe, kanonische Quellen, keine Secrets |
| AAIAM-Import-RC | `docs/.release-candidate/aaiam/aaiam-import-package.json` | `sourcePath`, `sourceVersion`, Redaction-Status, `dbWrite: false` |
| Release-Manifest | `docs/.release-candidate/release-manifest.json` | Quellcommit, Exportmanifest-Hash, Artefaktliste und Hashes |

## Wer freigeben darf?

- Maintainer oder Owner des Repositories.
- Keine automatische Freigabe durch KI, Script, CI oder Generator.
- Keine implizite Freigabe durch erfolgreichen Build oder erfolgreichen Conformance Guard.

## Pflichtprüfung

- `DOCUMENTATION_TRUTH_RULE.md` angewendet.
- `glossary/term-status.md` geprüft.
- Secrets, Tokens, private Schlüssel und private Pfade redigiert.
- Markdown-Links und JSON-Quellpfade geprüft.
- Quellenstatus und Zielgruppen geprüft.
- Version, Stand und Statuskennzeichnung geprüft.
- Release-Manifest-Hashes erneut geprüft.
- Website-Routen und Legacy-Aliase geprüft.
- PDF-Quellen geprüft.
- In-App-Kontexte geprüft.
- AAIAM-Importpaket geprüft.
- Keine produktiven Status ohne Freigabe.

## Was Freigabe nicht bedeutet

- Kein automatisches Website-Deployment.
- Kein automatischer AAIAM-Import.
- Keine automatische PDF-Veröffentlichung.
- Keine In-App-Hilfe-UI-Aktivierung.
- Keine Änderung an AIR, Runtime, MCP oder Marketplace.

## Gate-Dateien

- [`../export/manual-review-checklist.json`](../export/manual-review-checklist.json)
- [`../export/release-gate-manifest.json`](../export/release-gate-manifest.json)

Beide Dateien bleiben initial auf `pending`. `deploymentAllowed`, `importAllowed`,
`pdfPublicationAllowed` und `inAppPackagingAllowed` bleiben `false`, bis ein Maintainer oder
Owner das Gate manuell freigibt.
