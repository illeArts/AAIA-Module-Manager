# Phase 11.5.12 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`, `docs/scripts/`, `scripts/validate_docs_conformance.py`

## 1. Was wurde gebaut?

Der Execution-Adapter wurde für kontrollierte Zielbedingungen erweitert: Website-Staging,
lokale PDF-Finalisierung, In-App-Hilfepaket und AAIAM-Import-Dry-Run. Ohne approved Gate bleibt
alles blockiert.

## 2. Warum wurde es gebaut?

11.5.11 hat fail-closed Adapter vorbereitet. 11.5.12 macht daraus einen kontrollierten
Erstlauf mit Audit-Ausgabe, ohne Live-Veröffentlichung oder produktive AAIAM-Befüllung.

## 3. Welche Dateien wurden geändert?

- `docs/website-help/controlled-first-publication.md`
- `docs/website-help/approved-release-execution.md`
- `docs/export/release-execution-plan.json`
- `docs/scripts/execute_docs_release_candidate.py`
- `scripts/validate_docs_conformance.py`
- `docs/website-help/export-pipeline.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Gate-Voraussetzungen

Ausführung setzt `gateStatus: approved`, `approvedBy`, `approvedAtUtc`, passende Allow-Flags,
abgeschlossene Checklist, gültige RC-Manifest-Hashes und explizite Target-Konfiguration voraus.
Der aktuelle Stand bleibt `pending`; deshalb ist `EXECUTION: BLOCKED` korrekt.

## 5. Target-Verhalten

- Website: nur Staging, kein Live-Upload.
- PDF: lokale Finalisierung oder sauberer Skip.
- In-App-Hilfe: Paket, keine Aktivierung.
- AAIAM: Dry Run oder fail-closed.

## 6. AAIAM-Dry-Run-Regeln

AAIAM bleibt fail-closed, solange Bibliothek oder Zielkonfiguration fehlen. Markdown bleibt
kanonische Quelle. Historische Rohtexte bleiben gesperrt. Secrets, private Pfade, Schlüssel,
Tokens und offene Begriffslangformen dürfen nicht importiert werden.

## 7. Audit-Regeln

Jeder Adapterlauf schreibt `docs/.release-candidate/execution-audit.json` mit Zeitpunkt,
Operator, Quellcommit, Target, Modus, Ergebnis, Reason-Code, Artefakt-Hashes, Secret-/Pfad-
Prüfung und `notLiveDeployment: true`.

## 8. Sicherheitsregeln

- Keine automatische Freigabe.
- Kein Live-Website-Deployment.
- Keine produktive AAIAM-DB-Befüllung.
- Keine öffentliche PDF-Veröffentlichung.
- Keine In-App-Hilfe-Aktivierung.
- Keine neuen Runtime-, AIR-, MCP- oder Marketplace-Funktionen.

## 9. Tests/Conformance

- `python docs/scripts/export_docs_dry_run.py .`
- `python docs/scripts/generate_docs_release_candidate.py .`
- `python docs/scripts/review_docs_release_candidate.py .`
- `python docs/scripts/execute_docs_release_candidate.py . --target all --dry-run --staging-only --require-approved-gate`
- `python scripts/validate_docs_conformance.py .`
- `git diff --check`
- `dotnet build AAIA.ModuleManager.sln --no-restore`
- `dotnet test AAIA.ModuleManager.sln --no-restore`

## 10. Nicht-Ziele

- Kein freies Live-Website-Deployment.
- Keine produktive AAIAM-DB-Befüllung.
- Keine automatische Freigabe.
- Keine In-App-Hilfe-Aktivierung in der App.
- Keine neuen Runtime-, AIR-, MCP- oder Marketplace-Funktionen.
- Kein Import historischer Rohtexte ohne Prüfung.

## 11. Nächster Schritt

Entweder Phase 11.5.13 — echte Website-Staging-Review nach manueller Freigabe — oder eine
technische Folgephase außerhalb der Dokumentationspipeline.

## 12. Relevanz für Benutzerhandbuch

Keine Inhaltsänderung. Benutzerhandbuch-Ausgaben bleiben durch Gate und Audit geschützt.

## 13. Relevanz für Entwicklerdokumentation

Der Adapter zeigt Entwicklern den kontrollierten Pfad von RC zu Staging/Dry-Run, ohne
Produktivstatus zu setzen.

## 14. Relevanz für Administratorhandbuch

Audit-, Rollback- und Staging-Grenzen sind vorbereitet. Es wurde keine Betriebsintegration
aktiviert.

## 15. Relevanz für Webseite / öffentliche Hilfe

Website bleibt auf Staging begrenzt. Es gibt kein Live-Deployment und keine Domainänderung.

## 16. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Vor jedem Lauf Gate, Checklist, Execution-Plan, RC-Manifest und Conformance Guard lesen. KI
darf keine Freigabe setzen. Ohne approved Gate ist `EXECUTION: BLOCKED` der korrekte Zustand.
