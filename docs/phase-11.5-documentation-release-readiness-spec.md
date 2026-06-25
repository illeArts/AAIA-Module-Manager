# Phase 11.5 — Documentation & Release Readiness

> Status: Foundation, 11.5.2-Inhaltsmigration, 11.5.3-Kernhandbücher, 11.5.4-Ausgabestruktur und 11.5.5-AAIAM-Vorbereitung umgesetzt; Deployment offen
> Ziel: versionierbare, zielgruppengerechte und wiederverwendbare AAIA-Dokumentation

## 1. Zweck

AAIA-Dokumentation ist Bestandteil von Sicherheit, Support und Release-Qualität. Sie muss
Menschen und unterstützende KI-Systeme auf denselben geprüften Projektstand bringen. Die
Dokumentation wird deshalb nicht erst nach der Produktentwicklung erstellt, sondern ist ab
dieser Phase ein verpflichtendes Ergebnis jedes Inkrements.

## 2. Zielgruppen und Bereiche

| Bereich | Zielgruppe | Verantwortet |
|---|---|---|
| `user-manual/` | Anwender | Installation, Bedienung, Rollen, typische Abläufe |
| `developer-guide/` | ETWs und Entwickler | Module, SDK, Tests, Signatur, Veröffentlichung |
| `admin-guide/` | Betreiber und Administratoren | AAIAS, Persistenz, Recovery, Audit, Updates |
| `architecture/` | Maintainer und technische Prüfer | Systemgrenzen, AIR, Sicherheit, Datenflüsse |
| `troubleshooting/` | alle operativen Rollen | Fehlerbilder, Fehlercodes und sichere Behebung |
| `glossary/` | alle Zielgruppen | verbindliche Terminologie und Querverweise |
| `website-help/` | Content- und Web-Integration | kuratierte Einstiege für `/docs`, `/help`, `/handbuch` |
| `phases/` | Maintainer und KI-Assistenten | einheitliche Abschlussnachweise je Phase |

## 3. Veröffentlichungsziele

Die Markdown-Quellen sind kanonisch. Daraus sollen später abgeleitet werden:

- öffentliche Website-Hilfe unter `/handbuch`, `/docs` und `/help`,
- PDF-Handbücher mit Versions- und Veröffentlichungsstand,
- kontextsensitive In-App-Hilfe,
- begrenzte, versionierte KI-Handoff-Kontexte.

Die Ableitungen dürfen die kanonische Quelle nicht still verändern. Veröffentlichung,
Suche, PDF-Generierung und Website-Routing sind eigene spätere Inkremente.

## 4. Verbindliche Abschlussregel

Jede technische Phase erhält vor Abschluss eine Datei nach
`docs/phases/phase-<nummer>-final.md`. Grundlage ist
[`PHASE_FINAL_TEMPLATE.md`](phases/PHASE_FINAL_TEMPLATE.md). Offene oder nicht belegte
Angaben werden sichtbar markiert; sie dürfen nicht aus Chatverläufen erraten werden.

## 5. Qualitäts- und Sicherheitsregeln

- Keine Secrets, Tokens, Passwörter, privaten Schlüssel oder echten Zugangsdaten.
- Keine privaten Server-, Benutzer- oder Dateisystempfade.
- Keine sicherheitsrelevanten Behauptungen ohne Verweis auf Code, Test oder Spezifikation.
- Beispiele verwenden ausschließlich fiktive IDs, Hosts und Zugangsdaten.
- Zielgruppe, Produktversion und letzter geprüfter Stand müssen erkennbar sein.
- Veraltete Inhalte werden markiert oder ersetzt, nicht kommentarlos parallel geführt.
- Begriffe verwenden die Schreibweise des zentralen Glossars.
- Komponentenstatus und historische Aussagen folgen `DOCUMENTATION_TRUTH_RULE.md`.
- Website, PDF und In-App-Hilfe müssen auf dieselbe kanonische Quelle zurückführbar sein.

## 6. Inkremente

| Inkrement | Inhalt | Status |
|---|---|---|
| 11.5.1 | Struktur, Bereichsindizes, Glossar und Phasenabschluss-Vorlage | umgesetzt |
| 11.5.2 | Quelleninventar, Dokumentationswahrheit, Phase-10-Betriebswissen und Public-Help-Vorbereitung | umgesetzt |
| 11.5.3 | Benutzer-, Entwickler- und Admin-Kernpfade vervollständigen und externe Developer-Docs abgleichen | umgesetzt |
| 11.5.4 | Website-Routen, Suchindex, In-App-Mapping und PDF-/Release-Struktur vorbereiten | umgesetzt |
| 11.5.5 | AAIAM Knowledge Sync Preparation & Help Runtime Integration | umgesetzt |
| 11.5.6 | Deployment-/Generierungs-Pipelines, PDF- und In-App-Ausgabe sowie Release-Conformance | geplant |

## 7. Abnahmekriterien für die Foundation

- Alle acht Dokumentationsbereiche besitzen einen Einstieg.
- Das Glossar enthält die festgelegten AAIA-Kernbegriffe und kennzeichnet offene Definitionen.
- Die Abschlussvorlage enthält Benutzer-, Entwickler-, Admin-, Website- und KI-Relevanz.
- Bestehende Inhalte unter `docs/help/`, `docs/air/` und den Phasenspezifikationen bleiben
  auffindbar und werden nicht als bereits migriert ausgegeben.
- Die Zielroute des bestehenden Website-Buttons ist als `/handbuch` festgelegt; die konkrete
  Website-Änderung bleibt außerhalb dieses Repository-Checkpoints.
- Eine Prüfung auf offensichtliche Secrets und absolute private Pfade ist grün.

## 8. Nicht-Ziele der Foundation

- kein Website-Deployment,
- keine neue In-App-Hilfe-Implementierung,
- keine PDF-Generierung,
- keine neuen AIR-, MCP-, Marketplace- oder Runtime-Funktionen,
- keine ungeprüfte Übernahme historischer Texte als verbindliche Produktzusage.

## 9. Stand nach Phase 11.5.2

Phase 11.5.2 ergänzt das Quelleninventar und migriert Phase-10-Betriebswissen in die
Zielstruktur. Die öffentliche Website-Hilfe ist vorbereitet, aber nicht deployed. Linkprüfung,
PDF-Ausgabe, In-App-Hilfe und der externe Abgleich mit `aaia-developer-docs` bleiben spätere
Inkremente.

## 10. Stand nach Phase 11.5.3

Phase 11.5.3 ergänzt Kernpfade für Benutzer, Entwickler und Administratoren und gleicht
`aaia-developer-docs` für Modul-, Plugin-, Manifest-, Permission- und Konventionsgrundlagen ab.
Nicht belegte Deployment-Details werden nicht als öffentliche Produktzusage übernommen.

## 11. Stand nach Phase 11.5.4

Phase 11.5.4 bereitet öffentliche Routen, Legacy-Aliase, Suchindex, In-App-Hilfe-Mapping und
PDF-/Release-Paket-Reihenfolgen vor. Es findet kein Website-Deployment, keine PDF-Generierung
und keine In-App-Implementierung statt.

## 12. Stand nach Phase 11.5.5

Phase 11.5.5 plant AAIAM als späteren strukturierten Wissensspeicher für freigegebene
Dokumentationsinhalte ein. Markdown bleibt kanonische Quelle. AAIAM übernimmt später nur
validierte, redigierte, versionierte und klassifizierte Inhalte. Es findet keine produktive
DB-Befüllung und keine echte AAIAM-Bibliotheksintegration statt.
