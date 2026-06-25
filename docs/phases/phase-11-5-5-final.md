# Phase 11.5.5 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`

## 1. Was wurde gebaut?

AAIAM wurde als späterer strukturierter Wissensspeicher für freigegebene Dokumentationsinhalte
eingeplant. Ergänzt wurden Knowledge-Sync-Spezifikation, Import-Map, Fehlercode-/Reason-Code-
Referenz und maschinenlesbares In-App-Kontext-Mapping.

## 2. Warum wurde es gebaut?

Hilfe, Handbuch, Troubleshooting, Lösungen, Glossar und freigegebene KI-Handoff-Kontexte sollen
später nicht nur als Markdown und Website-Ausgabe existieren, sondern auch versioniert und
durchsuchbar in AAIAM nutzbar sein. Dabei darf AAIAM nicht zur zweiten Wahrheitsschicht werden.

## 3. Welche Dateien wurden geändert?

- `docs/website-help/aaiam-knowledge-sync.md`
- `docs/help/aaiam-import-map.json`
- `docs/help/error-code-reference.md`
- `docs/help/in-app-context-map.json`
- `docs/help/index.json`
- `docs/website-help/index.md`
- `docs/website-help/public-help-structure.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. AAIAM-Abgrenzung

Markdown unter `docs/` bleibt kanonische Quelle. AAIAM wird später importierter, versionierter
und durchsuchbarer Nutzspeicher. AAIAM darf keine ungeprüften Rohtexte, historischen Quellen
oder nicht belegten Produktbehauptungen als heutige Wahrheit übernehmen.

## 5. Import- und Redaktionsregeln

Vor Import in AAIAM sind erforderlich:

- Prüfung gegen `DOCUMENTATION_TRUTH_RULE.md`,
- Statusklassifikation als kanonisch, historisch, deprecated oder draft,
- Redaction/Sicherheitsprüfung,
- Quellenpfad und Version,
- Zielgruppe,
- Fehler-/Reason-Code-Zuordnung,
- Glossar-/Begriffstatusprüfung,
- explizite Importfreigabe.

## 6. Sicherheitsregeln

AAIAM darf keine Secrets, Tokens, Passwörter, privaten Schlüssel, echten Zugangsdaten,
privaten Benutzer-/Serverpfade, erfundenen Langformen oder ungeprüften Produktzusagen
übernehmen. KI-Handoff-Kontexte dürfen nur freigegeben und redigiert importiert werden.

## 7. Welche Tests müssen grün sein?

- `git diff --check`
- JSON-Parsing für `docs/help/index.json`, `docs/help/aaiam-import-map.json`,
  `docs/help/in-app-context-map.json` und `docs/website-help/legacy-aliases.json`
- relative Markdown-Linkprüfung für `docs/`
- Prüfung von JSON-`sourcePath`-Zielen
- Prüfung auf bekannte falsche ETW-Langform, abgelöste Developer-Guide-Pfade und
  Signatur-Dublettenbegriffe

Produktcode wurde nicht geändert; eine vollständige Code-Regression ist für diese Phase nicht
erforderlich.

## 8. Was darf nicht verletzt werden?

- Keine produktive AAIAM-DB-Befüllung.
- Keine echte AAIAM-Bibliotheksintegration.
- Keine Website-Suchimplementierung.
- Keine PDF-Generierung.
- Keine In-App-Hilfe-UI.
- Keine neuen AIR-, MCP-, Marketplace- oder Runtime-Funktionen.

## 9. Bekannte Grenzen / offene Punkte

- AAIAM-Bibliothek ist noch nicht eingebunden.
- Importvalidator und Schema-Tests fehlen.
- Fehlercode-Referenz ist vorbereitet, aber nicht automatisiert aus Code generiert.
- In-App-Kontext-Mapping ist nicht im Produkt verdrahtet.
- Redaction-Pipeline ist spezifiziert, aber nicht implementiert.

## 10. Nächster Schritt

Phase 11.5.6 sollte Import-Schema-Validierung, Redaction-Check, CI-Linkprüfung oder den ersten
echten Exportpfad vorbereiten. Eine produktive AAIAM-Befüllung erfolgt erst, wenn die Bibliothek
verfügbar und der Importpfad geprüft ist.

## 11. Relevanz für Benutzerhandbuch

Benutzerhandbuch-Seiten sind als spätere AAIAM-Importquellen vorgesehen, bleiben aber in
Markdown kanonisch.

## 12. Relevanz für Entwicklerdokumentation

Developer-Guide-Seiten, Runtime-Durability, KI-Handoff und Diagnose sind für spätere AAIAM-
Suche und In-App-Kontexte vorbereitet.

## 13. Relevanz für Administratorhandbuch

Admin- und Runtime-Recovery-Wissen wird als importierbarer Wissensbestand vorbereitet, inklusive
Reason-Code-Referenzen und sicheren Handlungen.

## 14. Relevanz für Webseite / öffentliche Hilfe

AAIAM ergänzt Website- und Help-Ausgaben als Wissensspeicher, ersetzt sie aber nicht. Website-
Routen und Suchindex bleiben abgeleitete Ausgaben aus Markdown.

## 15. KI-Handoff-Kontext für Claude/Codex/ChatGPT

AAIAM ist ab 11.5.5 in der Doku-Architektur eingeplant. Keine DB-Integration annehmen.
Markdown bleibt Wahrheit. Für AAIAM-Arbeit zuerst `docs/website-help/aaiam-knowledge-sync.md`,
`docs/help/aaiam-import-map.json`, `docs/help/error-code-reference.md`,
`docs/help/in-app-context-map.json`, `docs/DOCUMENTATION_TRUTH_RULE.md` und
`docs/glossary/term-status.md` lesen.
