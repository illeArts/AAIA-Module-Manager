# Phase 11.5.9 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `docs/scripts/`, `scripts/validate_docs_conformance.py`

## 1. Was wurde gebaut?

Ein lokaler Release-Candidate-Generator für Dokumentationsausgaben. Er liest das Exportmanifest
und erzeugt unter `docs/.release-candidate/` getrennte Pakete für Website, PDF-Quellen,
In-App-Hilfe und AAIAM-Importvorbereitung.

## 2. Warum wurde es gebaut?

11.5.8 hat lokale Vorschau-Artefakte erzeugt. 11.5.9 hebt diese Stufe auf ein reproduzierbares
RC-Paket mit Artefakt-Hashes, Quellcommit und Exportmanifest-Hash, ohne Veröffentlichung oder
produktive AAIAM-Befüllung.

## 3. Welche Dateien wurden geändert?

- `docs/scripts/generate_docs_release_candidate.py`
- `scripts/validate_docs_conformance.py`
- `.gitignore`
- `docs/website-help/export-pipeline.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche lokalen Artefakte entstehen?

- `docs/.release-candidate/website/`
- `docs/.release-candidate/pdf/`
- `docs/.release-candidate/in-app/help-contexts.json`
- `docs/.release-candidate/aaiam/aaiam-import-package.json`
- `docs/.release-candidate/release-manifest.json`

Diese Artefakte sind lokale RC-Ausgaben, keine kanonische Quelle und nicht versioniert.

## 5. Welche Architekturentscheidungen wurden getroffen?

`docs/.release-candidate/` ist vom Preview-Ordner getrennt. Website-RC nutzt die vorbereitete
Routing-Map und legt Legacy-Aliase bei. PDF-RC erzeugt kombinierte Markdown-Dateien; echte PDF-
Dateien sind optional und werden sauber übersprungen, wenn keine lokale Toolchain vorhanden ist.
AAIAM-RC setzt `dbWrite` auf `false`.

## 6. Welche Doku-Wahrheitsregeln gelten?

Markdown bleibt kanonisch. RC-Artefakte dürfen den Status nur als `release_candidate` oder
vorbereitend kennzeichnen. Sie dürfen keine produktiven Aussagen als deployed, generated oder
imported behaupten.

## 7. Welche Sicherheitsregeln gelten?

Der Conformance Guard prüft RC-Artefakte auf offensichtliche Secrets, private Pfade, aktive
Status, AAIAM-DB-Schreibzugriff, Website-Deployment-Behauptungen und Release-Manifest-Hashes.
`notDeployed` und `notImported` müssen gesetzt bleiben.

## 8. Welche Tests müssen grün sein?

- `python docs/scripts/generate_docs_release_candidate.py .`
- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`

## 9. Was darf nicht verletzt werden?

- Kein automatisches Website-Deployment.
- Keine produktive AAIAM-DB-Befüllung.
- Keine finale Veröffentlichung.
- Keine neuen Runtime-, AIR-, MCP- oder Marketplace-Funktionen.
- Keine automatische PDF-Pflicht, wenn die lokale Toolchain fehlt.
- Keine RC-Artefakte committen.

## 10. Bekannte Grenzen / offene Punkte

- Website-RC ist statisches HTML aus Markdown, keine produktive Website.
- PDF-RC ist primär Markdown-Quelle; PDF-Dateien sind optional.
- In-App-RC ist ein Paket, keine UI-Integration.
- AAIAM-RC ist ein Importpaket, kein Importlauf.

## 11. Nächster Schritt

Phase 11.5.10 sollte ein manuelles Review- und Deployment-Gate definieren. Erst danach darf ein
konkreter Veröffentlichungskanal kontrolliert aktiviert werden.

## 12. Relevanz für Benutzerhandbuch

Das Benutzerhandbuch wird als Website-RC und kombinierte Markdown-Quelle für spätere PDF-
Veröffentlichung vorbereitet.

## 13. Relevanz für Entwicklerdokumentation

Das Entwicklerhandbuch wird in Website-, PDF-Source-, In-App- und AAIAM-RC-Pakete übernommen,
ohne den Trust- oder Marketplace-Status zu verändern.

## 14. Relevanz für Administratorhandbuch

Adminseiten werden als Website-RC und PDF-Source-RC vorbereitet. Betreiberwissen bleibt
redigiert und ohne private Pfade.

## 15. Relevanz für Webseite / öffentliche Hilfe

Die Website-Ausgabe ist ein lokales RC-Paket für `/handbuch`, `/docs` und `/help`. Es gibt
keinen Upload, keine Domainänderung und keine öffentliche Route.

## 16. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor Ausgabe- oder Deploymentarbeiten `docs/scripts/generate_docs_release_candidate.py`,
`docs/export/export-manifest.json`, `docs/website-help/routing-map.md` und
`scripts/validate_docs_conformance.py` lesen. `docs/.release-candidate/` nicht committen und
nicht als produktive Veröffentlichung behandeln.
