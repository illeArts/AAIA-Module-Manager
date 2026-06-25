# Phase 11.5.4 — Abschlussdokumentation

> Status: abgeschlossen  
> Geprüfter Stand: 2026-06-25  
> Verantwortlicher Scope: `aaia-module-manager/docs`

## 1. Was wurde gebaut?

Öffentliche Help-Ausgaben wurden vorbereitet: Routing Map, Legacy-Aliase, kanonischer
Suchindex, In-App-Hilfe-Mapping und PDF-/Release-Paket-Struktur.

## 2. Warum wurde es gebaut?

Nach 11.5.2 und 11.5.3 waren Inhalte und Kernhandbücher vorhanden. 11.5.4 macht diese Inhalte
für Website, Suche, In-App-Hilfe und spätere PDF-/Release-Pakete planbar, ohne bereits zu
deployen oder Ausgaben zu generieren.

## 3. Welche Dateien wurden geändert?

- `docs/help/index.json`
- `docs/website-help/routing-map.md`
- `docs/website-help/legacy-aliases.json`
- `docs/website-help/in-app-help-map.md`
- `docs/website-help/pdf-release-package.md`
- `docs/website-help/index.md`
- `docs/website-help/public-help-structure.md`
- `docs/documentation-inventory.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/phases/index.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Website-Hilfe bleibt eine abgeleitete Ausgabe aus kanonischen Markdown-Quellen. Der Suchindex
enthält `sourceStatus`, Zielgruppen, Tags und stabile Routen. Legacy-Artikel werden nicht
gelöscht, sondern über Aliasplanung auf kanonische Zielseiten geführt.

## 5. Welche Doku-Wahrheitsregeln gelten?

- Kanonische Quelle bleibt `docs/`.
- `docs/DOCUMENTATION_TRUTH_RULE.md` gilt für alle Ausgaben.
- Legacy-Quellen sind als `legacy-source` markiert.
- Website, Suche, In-App-Hilfe und PDF dürfen keinen abweichenden Produktstatus behaupten.
- Nicht deployte Routen bleiben „prepared-not-deployed“.

## 6. Welche Sicherheitsregeln gelten?

Keine Secrets, Tokens, Passwörter, private Schlüssel, reale Zugangsdaten oder privaten
Benutzerpfade in Suchindex, Aliasdateien, In-App-Mapping oder PDF-Struktur. In-App-Hilfe darf
redigierte Fehlerdaten verwenden, aber keine privaten Kontexte an externe Ziele weitergeben.

## 7. Welche Tests müssen grün sein?

- `git diff --check`
- relative Markdown-Linkprüfung für `docs/`
- JSON-Parsing für `docs/help/index.json` und `docs/website-help/legacy-aliases.json`
- Prüfung auf bekannte abgelöste Developer-Guide-Pfade und Signatur-Dublettenbegriffe

Produktcode wurde nicht geändert; eine vollständige Code-Regression ist für diese Phase nicht
erforderlich.

## 8. Was darf nicht verletzt werden?

- Kein Website-Deployment.
- Keine PDF-Generierung.
- Keine In-App-Hilfe-Implementierung.
- Keine neuen Runtime-, AIR-, MCP- oder Marketplace-Features.
- Keine eigene Website-Wahrheit neben den kanonischen Markdown-Quellen.

## 9. Bekannte Grenzen / offene Punkte

- tatsächliche Website-Router-Implementierung fehlt,
- Suche ist nur als Index vorbereitet,
- PDF-Layout und Generierung fehlen,
- In-App-Hilfe-Mapping ist nicht im Produkt verdrahtet,
- automatische Linkprüfung ist noch kein CI-Schritt.

## 10. Nächster Schritt

Phase 11.5.5 sollte die Ausgabe-Conformance vorbereiten: CI-Linkprüfung, JSON-Schema/Validator,
Website-Export-Pipeline, PDF-Generierung oder In-App-Hilfe-Bundling — je nachdem, welcher
Ausgabekanal zuerst freigegeben wird.

## 11. Relevanz für Benutzerhandbuch

Benutzerhandbuch-Seiten haben stabile `/handbuch`-Routen und sind in Suchindex und PDF-
Reihenfolge aufgenommen.

## 12. Relevanz für Entwicklerdokumentation

Entwicklerseiten haben stabile `/docs`-Routen, Suchmetadaten, In-App-Mapping für Module-
Manager-Flows und PDF-Reihenfolge.

## 13. Relevanz für Administratorhandbuch

Adminseiten sind in `/docs/admin`-Routen, Suchindex und PDF-/Release-Struktur aufgenommen.

## 14. Relevanz für Webseite / öffentliche Hilfe

Website-Hilfe besitzt jetzt Routing Map, Aliasplanung, Suchindex und Startkarten. Deployment
bleibt außerhalb dieses Scopes.

## 15. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Für Ausgabekanäle zuerst `docs/website-help/routing-map.md`, `docs/help/index.json`,
`docs/website-help/in-app-help-map.md` und `docs/website-help/pdf-release-package.md` lesen.
Diese Dateien sind Planungsartefakte; sie implementieren keine Website, keine Suche, keine PDF-
Erzeugung und keine In-App-Hilfe. Produktstatus immer gegen die kanonischen Quellen prüfen.
