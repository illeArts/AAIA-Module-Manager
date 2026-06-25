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
| 11.5.6 | Documentation Release Conformance & Export Pipeline Preparation | umgesetzt |
| 11.5.7 | Export Pipeline Preparation | umgesetzt |
| 11.5.8 | Documentation Output Pipeline Dry Run | umgesetzt |
| 11.5.9 | Documentation Output Generation & Release Candidate Packaging | umgesetzt |
| 11.5.10 | Manual Review, Approval & Deployment Gate | umgesetzt |
| 11.5.11 | Approved Release Execution Adapter | umgesetzt |
| 11.5.12 | Controlled First Publication / AAIAM Import Dry-Run Against Real Library | umgesetzt |
| 11.5.13 | Documentation Pipeline Stabilization, Commit Readiness & Handoff | umgesetzt |
| 11.5.14 | Website Staging Review & Publication Readiness | umgesetzt |
| 11.5.15 | AAIAM Knowledge Library Integration & Search Foundation | geplant |

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

## 13. Stand nach Phase 11.5.6

Phase 11.5.6 macht die neuen Dokumentationsartefakte prüfbar. Ein Conformance-Guard validiert
Markdown-Links, JSON-Quellpfade, AAIAM-Importmap, In-App-Kontextmap, Legacy-Aliase,
abgelöste Doku-Pfade, Signatur-Dublettenbegriffe sowie offensichtliche Secret- und private
Pfad-Muster. Der Guard ist in den bestehenden Build-Workflow eingebunden. Es findet weiterhin
kein Website-Deployment, keine PDF-Generierung, keine In-App-Hilfe-UI und keine AAIAM-DB-
Befüllung statt.

## 14. Stand nach Phase 11.5.7

Phase 11.5.7 ergänzt ein Exportmanifest und JSON-Schemas für Help-Index, AAIAM-Importmap,
In-App-Kontextmap, Legacy-Aliase und Exportmanifest. Der Conformance-Guard prüft zusätzlich
Exportquellen und verhindert aktive Status wie deployed, generated oder imported. Die Phase
bereitet Exportpfade vor, führt aber keine Ausgabe aus.

## 15. Stand nach Phase 11.5.8

Phase 11.5.8 ergänzt einen lokalen Dry-Run-Exporter. Er liest das Exportmanifest, prüft
Quellpfade und erzeugt lokale Vorschau-Artefakte unter `docs/.preview/` für Website,
PDF-Source, In-App-Hilfe und AAIAM-Import. `docs/.preview/` ist nicht kanonisch, wird nicht
versioniert und darf keine produktiven Status wie deployed, generated oder imported behaupten.

## 16. Stand nach Phase 11.5.9

Phase 11.5.9 ergänzt einen lokalen Release-Candidate-Generator. Er erzeugt unter
`docs/.release-candidate/` getrennte Pakete für Website, PDF-Quellen, In-App-Hilfe und
AAIAM-Importvorbereitung. Das RC-Manifest enthält Quellcommit, Exportmanifest-Hash,
Artefaktliste und Artefakt-Hashes. Der Status bleibt `release_candidate`; `notDeployed` und
`notImported` bleiben verpflichtend. Es findet weiterhin kein Website-Deployment, keine finale
PDF-Veröffentlichung, keine In-App-Hilfe-UI und keine produktive AAIAM-DB-Befüllung statt.

## 17. Stand nach Phase 11.5.10

Phase 11.5.10 ergänzt ein manuelles Review- und Freigabe-Gate. Das Gate besteht aus einer
menschlichen Spezifikation, einer maschinenlesbaren Review-Checklist, einem Gate-Manifest und
einem lokalen Review-Helfer. Initial bleibt `gateStatus` auf `pending`; KI-Freigabe ist
ausgeschlossen. `deploymentAllowed`, `importAllowed`, `pdfPublicationAllowed` und
`inAppPackagingAllowed` bleiben `false`. Der Conformance Guard schützt diese Regeln und
verhindert produktive Status ohne menschlich freigegebenes Gate.

## 18. Stand nach Phase 11.5.11

Phase 11.5.11 ergänzt fail-closed Execution-Adapter für Website, PDF, In-App-Hilfe und AAIAM.
Der maschinenlesbare Ausführungsplan bleibt initial `blocked`, alle Targets sind deaktiviert
und `executionAllowed` bleibt `false`. Das lokale Execution-Script prüft Gate, Checklist,
RC-Manifest-Hashes und Target-Gate-Flags. Ohne approved Gate endet es mit `EXECUTION: BLOCKED`.
AAIAM bleibt blockiert, solange Bibliothek und Zielkonfiguration fehlen.

## 19. Stand nach Phase 11.5.12

Phase 11.5.12 erweitert den Execution-Adapter um kontrollierte Zielmodi für Website-Staging,
lokale PDF-Finalisierung, In-App-Hilfepaket und AAIAM-Import-Dry-Run. Der Default bleibt
blocked/dry-run. Ohne approved Gate wird nur ein Audit unter
`docs/.release-candidate/execution-audit.json` geschrieben; es findet kein Live-Deployment,
kein produktiver AAIAM-Import, keine öffentliche PDF-Veröffentlichung und keine In-App-
Aktivierung statt.

## 20. Stand nach Phase 11.5.13

Phase 11.5.13 stabilisiert die Dokumentationspipeline 11.5.1 bis 11.5.12. Sie ergänzt eine
Handoff-Datei und eine Commit-/PR-Readiness-Notiz, ohne neue Veröffentlichungspfade zu bauen.
Preview- und Release-Candidate-Artefakte bleiben lokal und ignored. Ohne approved Gate bleibt
Execution blockiert; KI darf keine Freigabe setzen.

## 21. Stand nach Phase 11.5.14

Phase 11.5.14 ergänzt einen geschützten Website-Staging-Review. Das Script
`docs/scripts/stage_website_help.py` kopiert die Website-RC-Artefakte lokal nach
`docs/.staging/website/`, prüft Routen und Legacy-Aliase und schreibt ein
`staging-manifest.json`. `docs/.staging/` bleibt ignored und nicht kanonisch. Es findet kein
Live-Deployment, kein Domainwechsel und kein Server-/WordPress-Upload statt.
