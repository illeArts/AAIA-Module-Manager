# Phase 11.5.14 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `docs/scripts/`, `scripts/validate_docs_conformance.py`

## 1. Was wurde gebaut?

Ein geschützter Website-Staging-Review für die vorbereiteten Website-RC-Artefakte. Der lokale
Staging-Helfer erzeugt `docs/.staging/website/`, prüft Routen und Legacy-Aliase und schreibt
ein Staging-Manifest.

## 2. Warum wurde es gebaut?

Die Website-Hilfe soll fachlich, redaktionell und technisch prüfbar werden, ohne live zu gehen.
Staging trennt lokale Prüfung von öffentlicher Veröffentlichung.

## 3. Welche Dateien wurden geändert?

- `docs/website-help/website-staging-review.md`
- `docs/export/website-staging-review-checklist.json`
- `docs/scripts/stage_website_help.py`
- `.gitignore`
- `scripts/validate_docs_conformance.py`
- `docs/website-help/export-pipeline.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

`docs/.staging/` ist ein lokaler Staging-Ordner, keine kanonische Quelle. Das Staging-Script
darf kopieren und prüfen, aber nicht deployen, keine Domain ändern und keinen Server-Upload
ausführen.

## 5. Sicherheitsregeln

- Kein Live-Deployment.
- Kein WordPress-, FTP-, SSH- oder Server-Upload.
- Kein `deployed`-Status.
- Keine Secrets, Tokens, privaten Schlüssel oder privaten Pfade.
- Keine KI-Freigabe.

## 6. Welche Tests müssen grün sein?

- `python docs/scripts/export_docs_dry_run.py .`
- `python docs/scripts/generate_docs_release_candidate.py .`
- `python docs/scripts/stage_website_help.py .`
- `python docs/scripts/review_docs_release_candidate.py .`
- `python docs/scripts/execute_docs_release_candidate.py . --target all --dry-run --staging-only --require-approved-gate`
- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`

## 7. Was darf nicht verletzt werden?

Staging darf nicht als Live, deployed oder veröffentlicht erscheinen. Lokale Staging-Artefakte
dürfen nicht versioniert werden.

## 8. Bekannte Grenzen / offene Punkte

Die Staging-Prüfung bleibt lokal. Es gibt keine öffentliche Website-Änderung, keine Such-
Implementierung und keine AAIAM-Integration in dieser Phase.

## 9. Nächster Schritt

Phase 11.5.15 — AAIAM Knowledge Library Integration & Search Foundation.

## 10. Relevanz für Benutzerhandbuch

Der `/handbuch`-Einstieg wird im lokalen Staging geprüft, aber nicht veröffentlicht.

## 11. Relevanz für Entwicklerdokumentation

Der `/docs`-Einstieg wird im lokalen Staging geprüft, aber nicht veröffentlicht.

## 12. Relevanz für Administratorhandbuch

Keine Admin-Funktion wurde aktiviert. Staging bleibt lokales Review-Artefakt.

## 13. Relevanz für Webseite / öffentliche Hilfe

Zielrouten `/handbuch`, `/docs` und `/help` werden lokal geprüft. Es gibt keinen Live-Upload.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor Website-Arbeiten `docs/website-help/website-staging-review.md`,
`docs/export/website-staging-review-checklist.json` und `docs/scripts/stage_website_help.py`
lesen. KI darf keine Freigabe setzen und kein Live-Deployment auslösen.
