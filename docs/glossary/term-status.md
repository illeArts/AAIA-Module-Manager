# AAIA-Begriffstatus

> Geprüfter Stand: 2026-06-24
> Regel: [`../DOCUMENTATION_TRUTH_RULE.md`](../DOCUMENTATION_TRUTH_RULE.md)

Diese Datei trennt historische Bedeutung, aktuelle Repository-Belege und öffentlichen
Dokumentationsstatus. Das zentrale [Glossar](index.md) enthält die lesbaren Kurzdefinitionen.

## Statusübersicht

| Begriff | Historische Bedeutung | Aktueller Status in diesem Repository |
|---|---|---|
| DUKI | operativer Desktop-/System-Operator | historisch belegt; keine aktuelle Implementierung nachgewiesen |
| BBK | Bibliotheks-/Wissensagent | historisch belegt; keine aktuelle Implementierung nachgewiesen |
| Prompti | Kommunikations- und Aufgabenübersetzung | historisch belegt; keine aktuelle Implementierung nachgewiesen |
| Lector | Voice-/Sprachschicht | historisch belegt; keine aktuelle Implementierung nachgewiesen |
| VSI | Visual System Intelligence | Langform und Rolle historisch belegt; keine aktuelle Implementierung nachgewiesen |
| ETW | Entwickler-/Erstellerrolle mit Trust-Verantwortung | aktuelle Rolle belegt; ausgeschriebene Langform offen |

## DUKI

- **Status:** Historisch belegt; aktuelle Implementierung offen.
- **Rekonstruierte Bedeutung:** operativer AAIA-Operator für kontrollierte Desktop- und
  Systemaktionen. DUKI führt freigegebene Aufgaben aus und ist nicht der Wissensagent.
- **Aktueller Repository-Beleg:** als möglicher AIR-Host in Architekturtexten erwähnt; kein
  Code-, UI-, API- oder Testnachweis für ein aktuelles DUKI-Produkt in diesem Repository.
- **Historische Quelle:** 234 Treffer in der übergebenen Alt-Doku-Auswertung, darunter
  Architektur-, Closure-, Plattform- und Handoff-Dokumente.
- **Zulässige Formulierung:** „DUKI bezeichnet historisch den operativen AAIA-Operator für
  kontrollierte Desktop- und Systemaktionen.“
- **Offen:** Langform und heutige Produktgrenze festlegen; aktuelle Repositories prüfen.

## BBK

- **Status:** Historisch belegt; aktuelle Implementierung offen.
- **Rekonstruierte Bedeutung:** Bibliotheks-/Wissensagent. Historische Kerntrennung:
  **BBK liefert Wissen, DUKI führt aus.**
- **Aktueller Repository-Beleg:** als möglicher AIR-Host erwähnt; kein aktueller
  Implementierungsnachweis in diesem Repository.
- **Historische Quelle:** 16 Treffer in DUKI-Abschluss- und Handoff-Quellen.
- **Zulässige Formulierung:** „BBK bezeichnet historisch den Bibliotheks- bzw.
  Wissensagenten von AAIA.“
- **Offen:** Langform, Datenquellen, Trust-Grenze und aktuelle Produktzuordnung.

## Prompti

- **Status:** Historisch als Konzept belegt; aktuelle Implementierung offen.
- **Rekonstruierte Bedeutung:** Kommunikations- und Aufgabenübersetzung nach der
  Gatekeeper-/Sicherheitsprüfung; übersetzt Benutzersprache in strukturierte WorkOrders oder
  Vorschläge. Prompti ist nicht selbst der Gatekeeper.
- **Aktueller Repository-Beleg:** kein Code-, UI-, API- oder Testnachweis in diesem Repository.
- **Historische Quelle:** 42 Treffer in Kommunikations-, Hybrid-, DUKI- und Lector-Quellen.
- **Zulässige Formulierung:** „Prompti bezeichnet historisch die Kommunikations- und
  Aufgabenübersetzungsschicht von AAIA.“
- **Offen:** Abgrenzung zu AIR Tasks/Workflows und aktueller Implementierungsstand.

## Lector

- **Status:** Historisch stark als geplante bzw. teilweise gebaute Komponente belegt;
  aktuelle Implementierung offen.
- **Rekonstruierte Bedeutung:** Voice-Schicht mit Mikrofon, Wake Word, STT,
  Sicherheitsübergabe und TTS/Antwort.
- **Aktueller Repository-Beleg:** kein Code-, UI-, API- oder Testnachweis in diesem Repository.
- **Historische Quelle:** 58 Treffer, überwiegend im Lector-Windows-Briefing.
- **Zulässige Formulierung:** „Lector bezeichnet historisch die Voice-/Sprachschicht von AAIA.“
- **Offen:** Plattformreife, Datenschutz, Audio-Lifecycle und heutige Architekturzuordnung.

## VSI

- **Status:** Langform und historische Rolle belegt; aktuelle Implementierung offen.
- **Rekonstruierte Bedeutung:** **Visual System Intelligence** für visuelle Analyse,
  Layout, Animation, grafische Reports und Design-Unterstützung.
- **Aktueller Repository-Beleg:** kein Code-, UI-, API- oder Testnachweis in diesem Repository.
- **Historische Quelle:** 66 Treffer in Architektur-, Strategie-, Sicherheits- und VSI-Texten.
- **Zulässige Formulierung:** „VSI steht historisch für Visual System Intelligence und
  bezeichnet die visuelle Analyse- und Design-Unterstützungsschicht von AAIA.“
- **Offen:** aktuelle Komponenten, erlaubte Entscheidungen und Sicherheitsgrenzen verifizieren.

## ETW

- **Status:** Entwickler-/Erstellerrolle aktuell belegt; Langform offen.
- **Bestätigte Bedeutung:** Rolle mit ETW-ID, lokaler Signaturprüfung und Verantwortung im
  Release-/Marketplace-Prozess.
- **Aktueller Repository-Beleg:** Module-Manager-Code und Hilfetexte für ETW-ID,
  `EtwLocalSigned`, `EtwLocalVerified` und Marketplace-Gates.
- **Historische Quelle:** nur zwei Treffer; für die heutige Rolle nicht maßgeblich.
- **Zulässige Formulierung:** „ETW bezeichnet die offizielle Entwickler-/Erstellerrolle im
  AAIA-Erweiterungs- und Marketplace-Prozess.“
- **Offen:** ausgeschriebene Langform fachlich festlegen und danach kanonisch ausrollen.

## Quellenlage

Die konkrete historische Sekundärquelle und ihre Grenzen sind unter
[`sources/old-docs-term-reconstruction-2026-06.md`](sources/old-docs-term-reconstruction-2026-06.md)
registriert. Für eine Hochstufung auf „implementiert“ müssen die aktuellen zuständigen
Produkt-Repositories separat geprüft werden.
