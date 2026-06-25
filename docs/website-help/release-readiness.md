# Documentation Release Readiness

> Geprüfter Stand: 2026-06-25  
> Status: commit-reif vorbereitet, nicht veröffentlicht  
> Scope: Commit-/PR-Readiness der Dokumentationspipeline

## Checks vor Commit oder PR

Vor Commit oder PR müssen diese Checks grün sein:

```powershell
python docs/scripts/export_docs_dry_run.py .
python docs/scripts/generate_docs_release_candidate.py .
python docs/scripts/review_docs_release_candidate.py .
python docs/scripts/execute_docs_release_candidate.py . --target all --dry-run --staging-only --require-approved-gate
python scripts/validate_docs_conformance.py .
git diff --check
dotnet build AAIA.ModuleManager.sln --no-restore
dotnet test AAIA.ModuleManager.sln --no-restore
```

Der Execution-Adapter darf ohne approved Gate mit `EXECUTION: BLOCKED` enden. Das ist der
erwartete sichere Zustand.

## Nicht einchecken

Diese lokalen Artefakte dürfen nicht versioniert werden:

- `docs/.preview/`
- `docs/.release-candidate/`
- `docs/.release-candidate/execution-audit.json`
- lokale Website-/PDF-/In-App-/AAIAM-Ausgabeordner

## Offene manuelle Freigaben

- Website-Deployment ist nicht freigegeben.
- AAIAM-Import ist nicht freigegeben.
- PDF-Veröffentlichung ist nicht freigegeben.
- In-App-Hilfe-Packaging ist nicht freigegeben.
- KI-Freigabe ist ausgeschlossen.

## Warum kein automatisches Deployment erfolgt

Die Pipeline trennt kanonische Markdown-Quelle, lokale Preview, lokalen Release Candidate,
manuelles Gate und Ausführung. Ohne approved Gate dürfen Adapter nur prüfen, blockieren und ein
Audit schreiben. Veröffentlichung, Import und Aktivierung sind spätere, explizit freigegebene
Schritte.
