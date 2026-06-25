# Phase 11.5 Documentation Pipeline Handoff

> Geprüfter Stand: 2026-06-25  
> Status: stabilisiert, nicht deployed, nicht imported  
> Scope: Dokumentationspipeline 11.5.1 bis 11.5.12

## Aktueller Stand

Phase 11.5.1 bis 11.5.12 sind umgesetzt und dokumentiert:

- 11.5.1: Dokumentationsstruktur, Bereichsindizes, Glossar und Abschlussvorlage.
- 11.5.2: Inventar, Dokumentationswahrheit und Phase-10-Betriebswissen.
- 11.5.3: Kernhandbücher für Benutzer, Entwickler und Administratoren.
- 11.5.4: Public-Help-Routing, Suchindex-Vorbereitung, In-App- und PDF-Struktur.
- 11.5.5: AAIAM Knowledge Sync Vorbereitung.
- 11.5.6: Documentation Conformance Guard.
- 11.5.7: Exportmanifest und JSON-Schemas.
- 11.5.8: lokaler Dry Run unter `docs/.preview/`.
- 11.5.9: lokales Release-Candidate-Paket unter `docs/.release-candidate/`.
- 11.5.10: manuelles Review- und Freigabe-Gate.
- 11.5.11: fail-closed Execution Adapter.
- 11.5.12: kontrollierter Erstlauf als blocked/dry-run mit Audit.

## Kanonische Quelle

Markdown unter `docs/` bleibt die kanonische Quelle. JSON-Dateien unter `docs/export/`,
`docs/help/`, `docs/website-help/` und `docs/schemas/` beschreiben vorbereitete Strukturen,
Mapps, Gates und Pläne. Generierte Ausgabeordner sind nicht kanonisch.

## Nur Preview oder Release Candidate

- `docs/.preview/` ist lokale Vorschau aus Phase 11.5.8.
- `docs/.release-candidate/` ist lokales RC-/Audit-Ausgabeverzeichnis ab Phase 11.5.9.
- `docs/.release-candidate/execution-audit.json` ist lokales Audit nach Adapterlauf.

Diese Ordner sind ignored und dürfen nicht versioniert werden.

## Gate-Regeln

- `docs/export/release-gate-manifest.json` bleibt initial `pending`.
- `requiresHumanApproval` bleibt `true`.
- `aiMayApprove` bleibt `false`.
- Ohne approved Gate bleiben `deploymentAllowed`, `importAllowed`, `pdfPublicationAllowed`
  und `inAppPackagingAllowed` `false`.
- KI darf keine Freigabe setzen.

## AAIAM-Regeln

AAIAM ist vorbereitet, aber nicht produktiv angebunden. AAIAM darf nur validierte, redigierte,
versionierte Inhalte aus kanonischen Markdown-Quellen erhalten. Ohne Bibliothek und explizite
Zielkonfiguration bleibt der Adapter fail-closed mit `aaiam_library_unavailable` oder
`target_config_missing`.

## Nicht erfolgt

- Kein Website-Live-Deployment.
- Kein produktiver AAIAM-Import.
- Keine finale PDF-Veröffentlichung.
- Keine In-App-Hilfe-Aktivierung.
- Keine neue Runtime-, AIR-, MCP- oder Marketplace-Funktion.

## Offene nächste Optionen

- Website-Staging-Review nach manueller Gate-Freigabe.
- Echte Suchimplementierung.
- PDF-Veröffentlichung mit separatem Gate.
- In-App-Hilfe-Integration.
- AAIAM-Bibliotheksintegration.
- Technische Folgephase außerhalb der Dokumentationspipeline.

## Pflichtchecks vor Übergabe

- `python docs/scripts/export_docs_dry_run.py .`
- `python docs/scripts/generate_docs_release_candidate.py .`
- `python docs/scripts/review_docs_release_candidate.py .`
- `python docs/scripts/execute_docs_release_candidate.py . --target all --dry-run --staging-only --require-approved-gate`
- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`
