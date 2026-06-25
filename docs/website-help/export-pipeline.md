# Export Pipeline Preparation

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, keine Ausgabe generiert  
> Scope: Website-, PDF-, In-App- und AAIAM-Exportplanung

Phase 11.5.7 bereitet Export-Artefakte vor, ohne einen Ausgabekanal produktiv zu aktivieren.
Phase 11.5.8 ergänzt einen lokalen Dry-Run-Exporter. Markdown bleibt die kanonische Quelle.

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
- keine PDF-Dateien,
- keine In-App-Hilfe-UI,
- keine produktive AAIAM-DB-Befüllung,
- keine neue Runtime-Funktion.
