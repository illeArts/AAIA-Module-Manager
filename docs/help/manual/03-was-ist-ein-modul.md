# Was ist ein Modul?

## Kurz erklärt

Ein **Modul** (auch Extension oder Plugin genannt) ist eine Erweiterung für den AAIAS-Server. Es fügt neue Funktionen hinzu, die Nutzer kaufen und auf ihrem eigenen AAIAS-Server installieren können.

Beispiele für mögliche Module:
- Ein Modul, das Spracheingabe verarbeitet
- Ein Modul, das automatisch Dokumente zusammenfasst
- Ein Modul, das Daten aus externen Quellen abruft
- Ein Modul, das spezifische Workflows automatisiert

## Wie kommt ein Modul zu einem Nutzer?

```
Entwickler (ETW) erstellt Modul
        ↓
AAIA Module Manager: Prüfen, Signieren, Hochladen
        ↓
Marketplace: Prüfung & Freigabe
        ↓
Nutzer kauft Modul
        ↓
Nutzer installiert auf eigenem AAIAS-Server
        ↓
Modul läuft auf AAIAS
```

## Unterschied: Modul vs. Plugin vs. Extension

In AAIA werden diese Begriffe teilweise synonym verwendet:

- **Extension / Modul** — die fertige `.aaiaext`-Datei, die auf AAIAS läuft
- **Plugin** — im Kontext des Module Managers: eine Erweiterung für das Werkzeug selbst (nicht für AAIAS)

Wenn du ein neues Modul entwickelst, entwickelst du eine **Extension**.

## Was steckt in einem Modul?

Ein Modul besteht aus:
- Kompiliertem Code (`.dll`-Dateien)
- Einer `aaia-extension.json` (Manifest — beschreibt das Modul)
- Optionalen Ressourcendateien (Bilder, Konfigurationen)
- Einer digitalen Signatur (nach der Signierung)

## Technische Details

Module werden in C# entwickelt und mit dem .NET 8 SDK kompiliert. Die fertige Datei hat die Endung `.aaiaext` und ist ein ZIP-Archiv mit spezifischer Struktur.

## Verwandte Themen

- Was ist AAIA?
- Ein neues Projekt erstellen
- Was ist eine `.aaiaext`-Datei?
