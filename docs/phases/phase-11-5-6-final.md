# Phase 11.5.6 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `scripts/`, `.github/workflows/build.yml`

## 1. Was wurde gebaut?

Ein Documentation Conformance Guard wurde ergänzt und in den bestehenden Build-Workflow
eingebunden. Er prüft Markdown-Links, JSON-Quellpfade, AAIAM-Importmap, In-App-Kontextmap,
Legacy-Aliase, alte Doku-Pfade, Signatur-Dublettenbegriffe sowie offensichtliche Secret- und
private Pfad-Muster.

## 2. Warum wurde es gebaut?

Phase 11.5.2 bis 11.5.5 haben viele neue Doku-, Help-, Website-, AAIAM- und In-App-Artefakte
eingeführt. Ohne automatisierte Conformance-Prüfung würden kaputte Links, ungültige JSON-
Strukturen, alte Begriffe oder unsichere Inhalte erst spät auffallen.

## 3. Welche Dateien wurden geändert?

- `scripts/validate_docs_conformance.py`
- `.github/workflows/build.yml`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Die Conformance-Prüfung bleibt repository-lokal und ohne externe Dependencies. Sie validiert
die vorbereiteten Ausgabeartefakte, erzeugt aber keine Website, keine PDF-Dateien, keine
In-App-Hilfe und keine AAIAM-Datenbankeinträge.

## 5. Welche Doku-Wahrheitsregeln gelten?

`DOCUMENTATION_TRUTH_RULE.md` bleibt verbindlich. Der Validator schützt zusätzlich gegen
bekannte abgelöste Developer-Guide-Pfade, Signatur-Dublettenbegriffe und nicht belegte
ETW-Langformulierungen.

## 6. Welche Sicherheitsregeln gelten?

Der Validator sucht nach offensichtlichen Secret-Mustern, privaten Schlüsselblöcken und
privaten Workspace-/Benutzerpfaden. Beispiele mit ausdrücklich fiktiven Pfaden bleiben zulässig.

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
- Keine AAIAM-Bibliotheksintegration.
- Keine Runtime-, AIR-, MCP- oder Marketplace-Featureänderung.

## 9. Bekannte Grenzen / offene Punkte

- Der Validator ist pragmatisch, kein vollständiger Markdown-/JSON-Schema-Compiler.
- Keine externe Linkprüfung gegen Internetziele.
- Kein automatischer Inhaltsvergleich zwischen Website und Markdown.
- Keine AAIAM-Schema-Validierung gegen eine echte Bibliothek.

## 10. Nächster Schritt

Phase 11.5.7 sollte je nach Freigabe eine echte Exportpipeline vorbereiten: Website-Export,
PDF-Generierung, In-App-Hilfe-Bundling oder AAIAM-Importvalidator gegen die verfügbare
Bibliothek.

## 11. Relevanz für Benutzerhandbuch

Benutzerhandbuch-Links und spätere Ausgabepfade werden automatisiert mitgeprüft.

## 12. Relevanz für Entwicklerdokumentation

Developer-Guide-Pfade, Signatur-/Trust-Trennung und AAIAM-/In-App-Kontexte werden gegen
Regressionen geschützt.

## 13. Relevanz für Administratorhandbuch

Admin- und Runtime-Recovery-Seiten sind in JSON-Quellpfad- und Linkprüfung eingebunden.

## 14. Relevanz für Webseite / öffentliche Hilfe

Routing, Legacy-Aliase, Suchindex, In-App-Kontextmap und AAIAM-Importmap werden im Build
validiert.

## 15. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor Änderungen an Doku-/Help-Artefakten `python scripts/validate_docs_conformance.py .`
ausführen. Fehler im Validator sind als Release-Blocker für Dokumentationsänderungen zu
behandeln, außer die Regel selbst ist bewusst und dokumentiert anzupassen.
