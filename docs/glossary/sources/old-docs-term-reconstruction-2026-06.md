# Quellenregister — Rekonstruktion aus alten AAIA-Dokumenten

> Quelltyp: historische Sekundärauswertung
> Übernommen: 2026-06-24
> Originaldatei: `AAIA_old_docs_term_reconstruction.zip`
> SHA-256: `E34463A83FD3B4A08BE89113BEC3307FC36AB997266420E122EADB447E5B39EC`

## Inhalt des übergebenen Pakets

- `AAIA_TERM_RECONSTRUCTION_FROM_OLD_DOCS.md`
- `OLD_DOCS_AUDIT.md`

Das Paket enthält nicht die 30 historischen Quelldateien selbst, sondern eine zuvor erstellte
Textsuche und Stichproben-Auswertung. Seine Aussagen sind deshalb für Begriffsrekonstruktion
nützlich, aber kein aktueller Code- oder Implementierungsnachweis.

## Rekonstruierte Befunde

| Begriff | Treffer | Historische Rollenbeschreibung |
|---|---:|---|
| DUKI | 234 | operativer Desktop-/System-Operator |
| BBK | 16 | Bibliotheks-/Wissensagent |
| Prompti | 42 | Kommunikations- und Aufgabenübersetzer |
| Lector | 58 | Voice-/Sprachschicht |
| VSI | 66 | Visual System Intelligence |
| ETW | 2 | historische Langform nicht belastbar |

## Genannte historische Dateien

Die Auswertung nennt unter anderem Architektur-, Decision-Log-, DUKI-Closure-, Plattform-,
Lector-, Kommunikations-, Security- und VSI-Dokumente. Diese Rohquellen sind in diesem
Repository nicht enthalten und wurden in diesem Checkpoint nicht unabhängig verifiziert.

## Verwendungsregel

- Bedeutung darf mit dem Zusatz „historisch“ rekonstruiert werden.
- Aktueller Implementierungsstatus bleibt offen, bis Code, API, UI und Tests geprüft wurden.
- Nicht belegte Langformen werden nicht aus Vorschlagslisten übernommen.
- Bei späterer Übernahme der Rohquellen werden Pfad, Version, Hash und Widersprüche ergänzt.
