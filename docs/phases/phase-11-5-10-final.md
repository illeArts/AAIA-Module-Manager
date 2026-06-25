# Phase 11.5.10 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `docs/scripts/`, `scripts/validate_docs_conformance.py`

## 1. Was wurde gebaut?

Ein manuelles Review-, Approval- und Deployment-Gate für Dokumentationsausgaben. Das Gate
verhindert, dass Release-Candidate-Artefakte aus `docs/.release-candidate/` automatisch
veröffentlicht, importiert oder als finale Ausgabe behandelt werden.

## 2. Warum wurde es gebaut?

11.5.9 erzeugt lokale Release-Candidate-Pakete. Diese Pakete brauchen eine kontrollierte
Freigabeschicht, bevor Website-, PDF-, In-App- oder AAIAM-Ausgaben ausgeführt werden dürfen.

## 3. Welche Dateien wurden geändert?

- `docs/website-help/manual-release-gate.md`
- `docs/export/manual-review-checklist.json`
- `docs/export/release-gate-manifest.json`
- `docs/scripts/review_docs_release_candidate.py`
- `scripts/validate_docs_conformance.py`
- `docs/website-help/export-pipeline.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Das Gate ist kein Deployment-Adapter. Es trennt manuelle Freigabe von technischer Ausführung.
Die Checklist hält Pflichtprüfungen maschinenlesbar fest. Das Gate-Manifest hält erlaubte
Folgeschritte fest und bleibt initial gesperrt. Das Review-Script prüft nur und setzt keine
Freigabe.

## 5. Welche Sicherheitsregeln gelten?

- `requiresHumanApproval` bleibt `true`.
- `aiMayApprove` bleibt `false`.
- `gateStatus` bleibt initial `pending`.
- `deploymentAllowed`, `importAllowed`, `pdfPublicationAllowed` und `inAppPackagingAllowed`
  bleiben ohne approved Gate `false`.
- AAIAM-Import bleibt ohne produktive DB-Befüllung.
- Website-RC bleibt ohne Upload, Domainänderung oder öffentliche Route.

## 6. Welche Tests müssen grün sein?

- `python docs/scripts/generate_docs_release_candidate.py .`
- `python docs/scripts/review_docs_release_candidate.py .`
- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`

## 7. Was darf nicht verletzt werden?

- Keine KI-basierte Freigabe.
- Kein automatisches Website-Deployment.
- Kein produktiver AAIAM-Import.
- Keine finale PDF-Veröffentlichung.
- Keine In-App-Hilfe-UI.
- Keine neuen Runtime-, AIR-, MCP- oder Marketplace-Funktionen.

## 8. Bekannte Grenzen / offene Punkte

Das Gate beschreibt und prüft Freigabebedingungen, führt aber keine Veröffentlichung aus. Ein
approved Gate muss später bewusst und menschlich gesetzt werden. Die technische Ausführung ist
Aufgabe einer späteren Phase.

## 9. Nächster Schritt

Phase 11.5.11 — Approved Release Execution Adapter. Diese Phase darf erst Ausführungsschritte
vorbereiten, wenn das Gate manuell freigegeben wurde.

## 10. Relevanz für Benutzerhandbuch

Keine Inhaltsänderung am Benutzerhandbuch. Das Gate schützt spätere Benutzerhandbuch-Ausgaben
vor ungeprüfter Veröffentlichung.

## 11. Relevanz für Entwicklerdokumentation

Keine neue Runtime- oder Entwicklerfunktion. Entwickler erhalten einen klaren Review- und
Freigaberahmen für Dokumentationsausgaben.

## 12. Relevanz für Administratorhandbuch

Keine Betriebsfunktion wurde geändert. Das Gate verhindert, dass Betriebsdokumentation ohne
manuelle Prüfung als veröffentlicht oder importiert gilt.

## 13. Relevanz für Webseite / öffentliche Hilfe

Zielrouten bleiben `/handbuch`, `/docs` und `/help`, aber es gibt kein Deployment und keine
öffentliche Aktivierung. Die Website-Hilfe beschreibt nur das manuelle Gate.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor jeder Ausgabe `docs/export/release-gate-manifest.json`,
`docs/export/manual-review-checklist.json`, `docs/website-help/manual-release-gate.md` und
`scripts/validate_docs_conformance.py` lesen. KI darf keine Freigabe setzen. Der nächste
technische Schritt ist ein Execution Adapter, aber nur nach menschlicher Freigabe.
