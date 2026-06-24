# AAIA-Dokumentationswahrheit

Diese Regel ist für Handbücher, Architekturtexte, Website-Hilfe, Phasenabschlüsse und
KI-Handoff-Kontexte verbindlich.

## Grundsatz

Dokumentation darf einen Begriff, ein Produkt oder eine Funktion nur dann als implementiert
beschreiben, wenn der behauptete Stand durch mindestens einen aktuellen Repository-Beleg
nachweisbar und mit den übrigen Systemgrenzen vereinbar ist.

Als Belege gelten insbesondere:

- ausführbarer Code mit klarer Zuständigkeit,
- öffentliche Contracts oder API-Routen,
- erreichbare UI mit zugehöriger Logik,
- grüne, aussagekräftige Tests,
- freigegebene Spezifikation für den Status „spezifiziert“, nicht „implementiert“.

Historische Dokumente, Handoffs, Roadmaps und Chatverläufe sind Quellen für Absicht und
Begriffsrekonstruktion, aber kein alleiniger Implementierungsnachweis.

## Pflichtstatus

Jede nicht triviale Komponente wird einem Status zugeordnet:

| Status | Bedeutung |
|---|---|
| Implementiert | Aktueller Code/Route/UI plus angemessener Test- oder Prüfnachweis vorhanden |
| Vorbereitet | Contracts, Infrastruktur oder Teilimplementierung vorhanden; kein vollständiger Produktpfad |
| Spezifiziert | Fachlich freigegebene Spezifikation vorhanden; Implementierung offen |
| Konzept / Roadmap | Beabsichtigte Rolle beschrieben; keine aktuelle Umsetzung behauptet |
| Historisch belegt | Bedeutung aus älteren Quellen rekonstruierbar; aktueller Status ungeprüft |
| Offene Definition | Bedeutung, Langform oder Systemgrenze nicht belastbar festgelegt |
| Veraltet | Früherer Stand, der dem aktuellen System nicht mehr entspricht |

Mehrere Statusangaben dürfen kombiniert werden, beispielsweise „historisch belegt;
aktueller Implementierungsstatus offen“.

## Formulierungsregeln

- „ist implementiert“ nur mit aktuellem Beleg verwenden.
- „soll“, „geplant“, „historisch“ und „spezifiziert“ nicht durch „ist“ ersetzen.
- Eine nicht belegte Langform niemals aus einer Abkürzung erraten.
- Widersprüche zwischen historischer Quelle, Spezifikation und Code sichtbar dokumentieren.
- Sicherheits- und Trust-Grenzen aus aktuellem Code und freigegebenen Spezifikationen ableiten.
- Veraltete Aussagen nicht still löschen; Herkunft, Ablösung und gültigen Nachfolger festhalten.

## Belegformat

Begriffstatusseiten nennen mindestens:

- kanonischer Begriff,
- Status,
- rekonstruierte oder bestätigte Bedeutung,
- aktuelle Repository-Belege,
- historische Quellen,
- zulässige Dokumentationsformulierung,
- offene Entscheidungen und verantwortlicher nächster Prüfschritt.

## Sicherheitsregel

Belege dürfen keine Secrets, Tokens, Passwörter, privaten Schlüssel, realen Zugangsdaten oder
privaten Server-/Benutzerpfade in die Dokumentation kopieren.
