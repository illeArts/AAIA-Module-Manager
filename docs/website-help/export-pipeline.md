# Export Pipeline Preparation

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, keine Ausgabe generiert  
> Scope: Website-, PDF-, In-App- und AAIAM-Exportplanung

Phase 11.5.7 bereitet Export-Artefakte vor, ohne einen Ausgabekanal produktiv zu aktivieren.
Phase 11.5.8 ergänzt einen lokalen Dry-Run-Exporter. Markdown bleibt die kanonische Quelle.
Phase 11.5.9 erzeugt lokale Release-Candidate-Artefakte, weiterhin ohne Deployment oder
AAIAM-DB-Import.
Phase 11.5.10 ergänzt ein manuelles Review- und Freigabe-Gate vor jeder Ausführung.
Phase 11.5.11 ergänzt fail-closed Execution-Adapter, die ohne approved Gate blockieren.

## Exportziele

| Export | Manifest-ID | Status |
|---|---|---|
| Website-Hilfe | `website-help` | vorbereitet, nicht deployed |
| Benutzerhandbuch | `user-manual` | vorbereitet, nicht generiert |
| Entwicklerhandbuch | `developer-guide` | vorbereitet, nicht generiert |
| Administratorhandbuch | `admin-guide` | vorbereitet, nicht generiert |
| Troubleshooting | `troubleshooting` | vorbereitet, nicht generiert |
| Glossar | `glossary` | vorbereitet, nicht generiert |
| AAIAM-Import | `aaiam-import` | vorbereitet, nicht importiert |

## Manifest

Das zentrale Exportmanifest liegt unter [`../export/export-manifest.json`](../export/export-manifest.json).
Es nennt Exportziel, Typ, Status, Route und kanonische Quellen.

## Lokaler Dry Run

Der Dry Run wird lokal ausgeführt:

```powershell
python docs/scripts/export_docs_dry_run.py .
```

Er erzeugt ausschließlich Vorschau-Artefakte unter `docs/.preview/`:

- `website/` — einfache statische HTML-Vorschau aus Markdown,
- `pdf-source/` — kombinierte Markdown-Quellen für spätere PDF-Erzeugung,
- `in-app/help-contexts.json` — Vorschaupaket für kontextsensitive Hilfe,
- `aaiam/aaiam-import-preview.json` — Vorschau eines AAIAM-Importpakets ohne DB-Schreibzugriff.

`docs/.preview/` ist nicht Teil der kanonischen Quelle und wird nicht eingecheckt.

## Lokaler Release Candidate

Der Release-Candidate-Generator wird lokal ausgeführt:

```powershell
python docs/scripts/generate_docs_release_candidate.py .
```

Er erzeugt ausschließlich lokale RC-Artefakte unter `docs/.release-candidate/`:

- `website/` — statische HTML-Struktur für `/handbuch`, `/docs` und `/help`,
- `website/routing-map.json` — abgeleitete lokale Routingliste,
- `website/legacy-aliases.json` — Legacy-Aliasplanung als Beilage,
- `pdf/` — kombinierte Markdown-Dateien für Benutzer-, Entwickler- und Adminhandbuch,
- `pdf/pdf-status.json` — PDF-Toolchain-Status; fehlende Toolchain ist ein sauberer Skip,
- `in-app/help-contexts.json` — lokales Hilfe-Kontextpaket mit kanonischen Quellen,
- `aaiam/aaiam-import-package.json` — Importpaket ohne DB-Schreibzugriff,
- `release-manifest.json` — RC-Manifest mit Hashes, Quellcommit und Exportmanifest-Hash.

`docs/.release-candidate/` ist nicht Teil der kanonischen Quelle, wird nicht eingecheckt und
darf nicht als öffentliche Veröffentlichung behandelt werden. Der Status bleibt
`release_candidate`; `notDeployed` und `notImported` müssen gesetzt bleiben.

## Manuelles Release-Gate

Das Gate ist unter [`manual-release-gate.md`](manual-release-gate.md) beschrieben.

Die maschinenlesbaren Gate-Dateien liegen hier:

- [`../export/manual-review-checklist.json`](../export/manual-review-checklist.json)
- [`../export/release-gate-manifest.json`](../export/release-gate-manifest.json)

Der Review-Helfer liest Checklist, Gate-Manifest und RC-Manifest:

```powershell
python docs/scripts/review_docs_release_candidate.py .
```

Das Script darf keine Freigabe setzen. Initial bleibt `gateStatus` auf `pending`.
`deploymentAllowed`, `importAllowed`, `pdfPublicationAllowed` und `inAppPackagingAllowed`
bleiben `false`, bis ein Maintainer oder Owner manuell freigibt.

## Approved Release Execution Adapter

Die Adapter-Spezifikation liegt unter
[`approved-release-execution.md`](approved-release-execution.md).

Der maschinenlesbare Ausführungsplan liegt unter
[`../export/release-execution-plan.json`](../export/release-execution-plan.json).

Der lokale Adapter wird so geprüft:

```powershell
python docs/scripts/execute_docs_release_candidate.py . --dry-run
```

Ohne approved Gate ist das erwartete Ergebnis `EXECUTION: BLOCKED`. Das Script darf keine
Freigabe setzen, keine Website deployen, keine AAIAM-DB befüllen, keine PDF veröffentlichen
und keine In-App-Hilfe aktivieren.

## Schemas

Vorbereitete Schemas liegen unter [`../schemas/`](../schemas/):

- `help-index.schema.json`
- `aaiam-import-map.schema.json`
- `in-app-context-map.schema.json`
- `legacy-aliases.schema.json`
- `export-manifest.schema.json`

Die Schemas dokumentieren die erwartete Struktur. Der lokale Validator prüft eine pragmatische
Teilmenge ohne externe Dependencies.

## Nicht-Ziele

- kein Website-Deployment,
- keine verpflichtende PDF-Erzeugung,
- keine In-App-Hilfe-UI,
- keine produktive AAIAM-DB-Befüllung,
- keine neue Runtime-Funktion,
- keine KI-basierte Freigabe,
- keine Adapter-Ausführung ohne approved Gate.
