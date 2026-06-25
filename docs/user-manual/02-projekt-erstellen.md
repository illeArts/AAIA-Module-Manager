# Projekt erstellen

> Zielgruppe: ETWs und Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: migrierter Benutzerpfad; Produktdetails können je nach Release variieren

Ein neues Modul oder Plugin beginnt mit einer klaren Idee, einem Zielhost und einem gültigen
Manifest. Die KI-Unterstützung des Module Managers kann Vorschläge machen, trifft aber keine
endgültige Sicherheits- oder Architekturentscheidung.

## Vor dem Start klären

1. Soll die Erweiterung auf AAIAS laufen oder im AAIAC-Client?
2. Wird ein Modul, Plugin oder später ein Hybrid-Paket benötigt?
3. Welche Permissions sind wirklich erforderlich?
4. Gibt es externe Dienste, Dateien oder Netzwerkziele?
5. Welche Lizenz- und Marketplace-Anforderungen gelten?

## Typischer Ablauf

1. Neue Idee im Module Manager beschreiben.
2. Vorschlag für Projekttyp und Namen prüfen.
3. Zielhost festlegen: `AAIAS` für serverseitige Module, `AAIAC` für Client-Plugins.
4. Projektstruktur erzeugen lassen.
5. Manifest prüfen und bei Bedarf manuell korrigieren.
6. Validierung ausführen, bevor Build oder Paketierung gestartet werden.

## Ergebnis

Ein Projekt enthält typischerweise:

- Quellcodeprojekt,
- Einstiegspunkt für Modul oder Plugin,
- `aaia-extension.json`,
- optionale Ressourcen,
- lokale README oder Entwicklernotizen.

## Sicherheitsregel

KI-Vorschläge sind Vorschläge. Sie dürfen keine ungeprüften Permissions, Secrets oder privaten
Pfade in das Projekt schreiben. Auto-Fix- oder KI-Änderungen müssen sichtbar bestätigt werden.
