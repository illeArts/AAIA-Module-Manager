# Phase 11.5.1 — Abschlussdokumentation

> Status: abgeschlossen
> Geprüfter Stand: 2026-06-24
> Verantwortlicher Scope: `aaia-module-manager/docs`

## 1. Was wurde gebaut?

Eine kanonische Dokumentationsstruktur mit acht Zielbereichen, zentralem Glossar,
Phasenabschluss-Vorlage, ersten Einstiegsseiten und einer Website-Routenplanung.

## 2. Warum wurde es gebaut?

AAIA benötigt konsistente Terminologie und überprüfbare Dokumentation als Sicherheits-,
Support- und Entwicklungsbestandteil. Handoffs und Chatverläufe reichen dafür nicht aus.

## 3. Welche Dateien wurden geändert?

- `docs/README.md`
- `docs/phase-11.5-documentation-release-readiness-spec.md`
- `docs/{user-manual,developer-guide,admin-guide,architecture}/`
- `docs/{troubleshooting,glossary,website-help,phases}/`
- `docs/air/HANDOFF.md`

## 4. Welche Architekturentscheidungen wurden getroffen?

Markdown bleibt kanonische Quelle. Website, PDF, In-App-Hilfe und KI-Kontexte sind abgeleitete,
versionierte Ausgaben. Zielgruppeninhalte werden getrennt; das Glossar ist terminologisch
verbindlich. Vorhandene Texte werden als Quellen inventarisiert statt ungeprüft dupliziert.

## 5. Welche Sicherheitsregeln gelten?

Keine Secrets, Tokens, Passwörter, privaten Schlüssel, realen Zugangsdaten oder privaten
Server-/Benutzerpfade. Sicherheitsbehauptungen benötigen eine überprüfbare Quelle.

## 6. Welche Tests müssen grün sein?

- alle relativen Links in den neuen/geänderten Markdown-Dateien sind auflösbar,
- Secret-Pattern-Scan ohne Befund,
- `git diff --check` ohne Befund.

Es wurde kein Produktcode geändert; die bestehende Code-Regression bleibt 295/295.

## 7. Was darf nicht verletzt werden?

- AIR- und App-Grenzen dürfen in vereinfachten Texten nicht vermischt werden.
- Lokale und serverseitige Trust-Stufen bleiben getrennt.
- Offene Produktdefinitionen dürfen nicht als Fakten ausgegeben werden.
- Der bestehende Phase-10-Implementierungsscope bleibt unverändert.

## 8. Bekannte Grenzen / offene Punkte

- DUKI, BBK, Prompti, Lector, VSI und die ETW-Langform benötigen fachliche Definitionen.
- Bestehende Texte unter `docs/help/` und `aaia-developer-docs` sind noch nicht inventarisiert.
- Website-Routen, Suche, Versionierung, PDF und In-App-Ausgabe sind noch nicht implementiert.

## 9. Nächster Schritt

Phase 10 technisch fortsetzen. Für Dokumentation folgt 11.5.2 mit Quelleninventar,
Dokumentbesitzern, Versionsmetadaten und automatischer Linkprüfung.

## 10. Relevanz für Benutzerhandbuch

Einstieg und Zielstruktur sind vorhanden; Installation, Verbindung, Rollen und vollständige
Fehlerbehebung bleiben offen.

## 11. Relevanz für Entwicklerdokumentation

ETW-Einstieg und Zielstruktur sind vorhanden. Modul-, SDK-, Test-, Signatur- und
Marketplace-Anleitungen müssen mit `aaia-developer-docs` abgeglichen werden.

## 12. Relevanz für Administratorhandbuch

Ein sicherer AAIAS-Vorbereitungsrahmen ist vorhanden; verbindliche Installations-, Netzwerk-,
Persistenz-, Backup-, Recovery- und Updateanleitungen fehlen noch.

## 13. Relevanz für Webseite / öffentliche Hilfe

`/handbuch`, `/docs` und `/help` sind als Zielrouten getrennt. Der vorhandene
„Dokumentation“-Button soll `/handbuch` öffnen; Website-Umsetzung und Deployment sind offen.

## 14. KI-Handoff-Kontext für Claude/Codex/ChatGPT

Dokumentation ist ab sofort verpflichtendes Phasenergebnis. `docs/README.md` ist der Einstieg,
`docs/glossary/index.md` die Terminologiequelle und
`docs/phases/PHASE_FINAL_TEMPLATE.md` die Abschlussregel. Unbestätigte Definitionen nicht
erraten. Keine Secrets oder privaten Pfade aufnehmen. Der technische nächste Schritt bleibt
die Phase-10-Mutationstransaktion und Snapshot-Bündelung.
