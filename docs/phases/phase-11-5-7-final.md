# Phase 11.5.7 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `scripts/validate_docs_conformance.py`

## 1. Was wurde gebaut?

Eine vorbereitete Exportstruktur für Website-Hilfe, Benutzerhandbuch, Entwicklerhandbuch,
Administratorhandbuch, Troubleshooting, Glossar und AAIAM-Import. Dazu kamen ein zentrales
Exportmanifest, JSON-Schemas und eine Erweiterung des Documentation Conformance Guards.

## 2. Warum wurde es gebaut?

Die Dokumentationsinhalte sind strukturiert und prüfbar. Für spätere Ausgabekanäle braucht es
nun eine klare, versionierbare Exportplanung, bevor Website, PDF, In-App-Hilfe oder AAIAM-
Import produktiv umgesetzt werden.

## 3. Welche Dateien wurden geändert?

- `docs/export/export-manifest.json`
- `docs/schemas/*.schema.json`
- `docs/website-help/export-pipeline.md`
- `scripts/validate_docs_conformance.py`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/website-help/index.md`
- `docs/website-help/public-help-structure.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Exportplanung wird als Manifest beschrieben. Schemas dokumentieren die erwarteten JSON-
Strukturen. Der Validator prüft bewusst ohne externe Dependencies nur die lokalen
Conformance-Regeln und Quellpfade. Aktive Ausgabezustände bleiben verboten.

## 5. Welche Doku-Wahrheitsregeln gelten?

Markdown bleibt kanonische Quelle. Export-Artefakte dürfen keinen eigenen Produktstatus
erfinden. `deployed`, `generated` und `imported` sind in dieser Phase nicht zulässige
Exportzustände.

## 6. Welche Sicherheitsregeln gelten?

Exportmanifest, Schemas und Exportdoku dürfen keine Secrets, Tokens, privaten Schlüssel,
echten Zugangsdaten oder privaten Benutzer-/Serverpfade enthalten. AAIAM-Import bleibt
vorbereitet, nicht produktiv.

## 7. Welche Tests müssen grün sein?

- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`

## 8. Was darf nicht verletzt werden?

- Kein Website-Deployment.
- Keine PDF-Generierung.
- Keine In-App-Hilfe-UI.
- Keine produktive AAIAM-DB-Befüllung.
- Keine Runtime-, AIR-, MCP- oder Marketplace-Featureänderung.

## 9. Bekannte Grenzen / offene Punkte

- JSON-Schemas sind dokumentiert, aber noch nicht mit einem externen JSON-Schema-Validator im
  Build ausgewertet.
- Keine externen Links werden geprüft.
- Kein Exporter erzeugt echte Website-, PDF- oder In-App-Artefakte.
- AAIAM-Import bleibt ohne Bibliotheksbindung.

## 10. Nächster Schritt

Phase 11.5.8 sollte den ersten echten Exportpfad auswählen: Website-Export, PDF-Generierung,
In-App-Hilfe-Bundling oder AAIAM-Importvalidierung gegen die verfügbare Bibliothek.

## 11. Relevanz für Benutzerhandbuch

Benutzerhandbuch-Seiten sind im Exportmanifest als eigener Export `user-manual` aufgeführt.

## 12. Relevanz für Entwicklerdokumentation

Entwicklerseiten sind im Exportmanifest als `developer-guide` aufgeführt und bleiben über
Conformance-Regeln gegen Pfad-Regressionen geschützt.

## 13. Relevanz für Administratorhandbuch

Adminseiten sind als `admin-guide`-Export geplant.

## 14. Relevanz für Webseite / öffentliche Hilfe

Website-Hilfe, Legacy-Aliase, Suchindex und Exportmanifest bilden jetzt die vorbereitete
Exportgrundlage.

## 15. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor Exportarbeiten `docs/export/export-manifest.json`, `docs/schemas/`,
`docs/website-help/export-pipeline.md` und `scripts/validate_docs_conformance.py` lesen.
Keine Ausgabe als deployed/generated/imported markieren, solange keine echte Pipeline
implementiert und geprüft wurde.
