# Phase 11.5.2 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`

## 1. Was wurde gebaut?

Eine kontrollierte Dokumentationsmigration für den aktuellen Stand: Quelleninventar,
Zielgruppenhandbücher, AIR-/Runtime-Betriebswissen, Architekturzusammenfassung,
Troubleshooting-Struktur und Website-Hilfe-Vorbereitung.

## 2. Warum wurde es gebaut?

Phase 11.5.1 legte nur die Dokumentations-Foundation an. Phase 10 ist technisch wertvoll,
aber ohne verständliche, zielgruppengerechte und überprüfbare Dokumentation nicht sicher
nutzbar. 11.5.2 macht den bestehenden Stand dokumentarisch verwendbar, ohne neue
Runtime-Features einzuführen.

## 3. Welche Dateien wurden geändert?

- `docs/documentation-inventory.md`
- `docs/user-manual/`
- `docs/developer-guide/`
- `docs/admin-guide/`
- `docs/architecture/`
- `docs/troubleshooting/`
- `docs/website-help/`
- `docs/glossary/term-status.md`
- `docs/help/manual/04-rollen.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Markdown bleibt kanonisch. Website-Hilfe wird als kuratierte Ableitung vorbereitet, nicht als
eigene Wahrheitsschicht. Phase-10-Betriebswissen wird nach Zielgruppen getrennt: Anwender
erhalten Sicherheits- und Laufzeitstatus, Entwickler Runtime-/Tool-Regeln, Administratoren
Recovery-/Protector-/Rollback-Regeln und Maintainer die Architekturgrenzen.

Die kanonischen Runtime-Seiten für 11.5.2 sind:

- `docs/troubleshooting/runtime-state-und-air.md`
- `docs/architecture/air-runtime-state.md`
- `docs/developer-guide/runtime-durability.md`
- `docs/documentation-inventory.md`

Als Nachzug wurden im Entwicklerhandbuch zusätzlich die Querschnittsthemen aus Rollen,
Signatur, Marketplace, KI-Handoff/Connector und Sicherheit/Laufzeitstatus kanonisch ergänzt:

- `docs/developer-guide/05-rollen-rechte-und-verantwortung.md`
- `docs/developer-guide/06-signatur-und-trust-level.md`
- `docs/developer-guide/07-marketplace-upload.md`
- `docs/developer-guide/08-ki-handoff-und-connector.md`
- `docs/developer-guide/09-fehleranalyse-und-diagnose.md`
- `docs/developer-guide/10-sicherheit-und-laufzeitstatus.md`

## 5. Welche Sicherheitsregeln gelten?

Keine Secrets, Tokens, Passwörter, privaten Schlüssel, realen Zugangsdaten oder privaten
Server-/Benutzerpfade. Fail-closed Verhalten, Protector-Grenzen und Trust-Stufen dürfen in
vereinfachten Texten nicht abgeschwächt werden.

## 6. Welche Tests müssen grün sein?

- Markdown-Links der neuen/geänderten Dateien müssen auflösbar sein.
- `git diff --check` muss ohne Befund laufen.
- Kein Produktcode wurde geändert; eine vollständige Code-Regression ist für diese Phase nicht
  erforderlich.

## 7. Was darf nicht verletzt werden?

- ETW-Langform bleibt offen und wird nicht erfunden.
- Historische Hilfetexte bleiben Quellen, aber nicht automatisch kanonische Fakten.
- Website-Deployment, Suche, PDF und In-App-Hilfe werden nicht vorweggenommen.
- Keine neue AIR-, MCP-, Marketplace- oder Runtime-Funktion wird eingeführt.

## 8. Bekannte Grenzen / offene Punkte

- Externes Repository `aaia-developer-docs` muss noch abgeglichen werden.
- Automatische Linkprüfung ist noch kein Build-Schritt.
- Öffentliche Website-Routen sind vorbereitet, aber nicht deployed.
- PDF- und In-App-Ausgaben fehlen weiterhin.

## 9. Nächster Schritt

Phase 11.5.3 sollte die Kernpfade weiter vervollständigen: Installation, Modul-/Plugin-
Entwicklung, Signatur-/Release-Pfad, Admin-Betrieb, Fehlercode-Referenz und Abgleich mit
externen Developer-Docs.

## 10. Relevanz für Benutzerhandbuch

Das Benutzerhandbuch enthält jetzt eine Seite zu Sicherheit und Laufzeitstatus. Sie erklärt
AIR-Persistenz, Recovery, Operation-IDs und sichere Support-Informationen ohne interne Pfade.

## 11. Relevanz für Entwicklerdokumentation

Das Entwicklerhandbuch enthält jetzt AIR-Runtime-/Tool-Regeln und eine eigene
Runtime-Durability-Seite: app-neutrale Grenzen, Operation-IDs, typed Deltas, Readiness,
Snapshot-/Recovery-Regeln und Testanforderungen. Zusätzlich sind Rollen/Rechte,
Signatur/Trust-Level, Marketplace-Upload, KI-Handoff/Connector, Fehleranalyse/Diagnose und
Sicherheit/Laufzeitstatus als Entwicklerseiten verankert.

## 12. Relevanz für Administratorhandbuch

Das Administratorhandbuch enthält jetzt Phase-10-Betriebswissen zu Aktivierung, Migration,
Rollback, Protectoren, Backpressure, Shutdown und Recovery.

## 13. Relevanz für Webseite / öffentliche Hilfe

`website-help/public-help-structure.md` definiert vorbereitete Routen, Startkarten,
kanonische Quellen und Veröffentlichungsregeln. Deployment bleibt außerhalb dieses Scopes.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Für weitere Arbeit zuerst `docs/documentation-inventory.md`,
`docs/DOCUMENTATION_TRUTH_RULE.md`, `docs/glossary/term-status.md` und die relevanten
Zielgruppenhandbücher lesen. Phase 11.5.2 migriert Inhalte kontrolliert, implementiert aber
keine neuen Produktfunktionen. Bei Begriffen mit offenem Status keine Langformen oder
Implementierung behaupten. Phase 10 gilt technisch abgeschlossen und ist jetzt in Handbuch,
Admin-Betrieb, Entwicklerdoku, Architektur, Troubleshooting und Website-Hilfe referenziert.
