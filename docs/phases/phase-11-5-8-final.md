# Phase 11.5.8 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `docs/scripts/`, `scripts/validate_docs_conformance.py`

## 1. Was wurde gebaut?

Ein lokaler Documentation Output Pipeline Dry Run. Der neue Exporter liest das Exportmanifest
und erzeugt kontrollierte Vorschau-Artefakte unter `docs/.preview/` für Website, PDF-Source,
In-App-Hilfe und AAIAM-Import.

## 2. Warum wurde es gebaut?

11.5.7 hat Exportpfade vorbereitet. 11.5.8 führt erstmals eine lokale Ausgabeprobe aus, ohne
produktive Veröffentlichung, DB-Schreibzugriff oder UI-Integration.

## 3. Welche Dateien wurden geändert?

- `docs/scripts/export_docs_dry_run.py`
- `scripts/validate_docs_conformance.py`
- `.gitignore`
- `docs/website-help/export-pipeline.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

`docs/.preview/` ist ein lokaler Vorschauordner, keine kanonische Quelle. Preview-Artefakte
verwenden den Status `preview-only`. AAIAM-Preview setzt `dbWrite` auf `false`.

## 5. Welche Doku-Wahrheitsregeln gelten?

Markdown bleibt kanonisch. Preview-Artefakte dürfen keine Aussage als deployed, generated oder
imported markieren. Der Dry Run darf nur Quellen aus dem Exportmanifest verwenden.

## 6. Welche Sicherheitsregeln gelten?

Preview-Dateien werden durch den Conformance Guard auf offensichtliche Secrets, private
Schlüssel und private Pfade geprüft. AAIAM bleibt Vorschau und schreibt nicht in eine DB.

## 7. Welche Tests müssen grün sein?

- `python docs/scripts/export_docs_dry_run.py .`
- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`

## 8. Was darf nicht verletzt werden?

- Kein Website-Deployment.
- Keine finale PDF-Erzeugung oder Veröffentlichung.
- Keine In-App-Hilfe-UI.
- Keine produktive AAIAM-DB-Befüllung.
- Keine neuen AIR-, MCP-, Marketplace- oder Runtime-Funktionen.
- Keine Preview-Dateien committen.

## 9. Bekannte Grenzen / offene Punkte

- Website-Preview ist bewusst einfache statische HTML-Vorschau.
- PDF-Source ist kombinierte Markdown-Quelle, kein PDF.
- In-App-Paket ist nicht im Produkt verdrahtet.
- AAIAM-Preview ist kein Import und keine DB-Transaktion.

## 10. Nächster Schritt

Phase 11.5.9 sollte einen Ausgabekanal auswählen und technisch vorbereiten: Website-Export,
PDF-Generierung, In-App-Hilfe-Bundling oder AAIAM-Importvalidierung gegen die verfügbare
Bibliothek.

## 11. Relevanz für Benutzerhandbuch

Benutzerhandbuchseiten werden im Dry Run als Website- und PDF-Source-Vorschau erzeugt.

## 12. Relevanz für Entwicklerdokumentation

Entwicklerhandbuchseiten werden als Preview exportiert und in In-App-/AAIAM-Kontexte
einbezogen, ohne produktive Veröffentlichung.

## 13. Relevanz für Administratorhandbuch

Adminseiten werden als PDF-Source- und Website-Preview vorbereitet.

## 14. Relevanz für Webseite / öffentliche Hilfe

Die Website-Vorschau zeigt lokale HTML-Dateien, aber keine öffentliche Route wird aktiviert.

## 15. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor Exportarbeiten `docs/scripts/export_docs_dry_run.py`, `docs/export/export-manifest.json`
und `scripts/validate_docs_conformance.py` lesen. Preview-Artefakte nicht committen und nicht
als produktive Ausgabe behandeln.
