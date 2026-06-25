# Validierung, Build und Paketierung

> Zielgruppe: ETWs und Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: migrierter Kernpfad aus vorhandener Hilfe

Validierung, Build und Paketierung bilden die lokale Vorprüfung. Sie sollen Fehler früh
sichtbar machen, bevor ein Paket signiert oder hochgeladen wird.

## Validierung

Die Validierung prüft unter anderem:

- Manifest vorhanden und lesbar,
- Pflichtfelder gesetzt,
- Version im erwarteten Format,
- Zielhost und Erweiterungstyp plausibel,
- Permissions deklariert,
- keine offensichtlich verbotenen Dateien im Paketpfad.

Fehler blockieren den nächsten Schritt. Warnungen sind kein sofortiger Blocker, müssen vor
einem Release aber bewusst bewertet werden.

## Build

Der Build kompiliert den Quellcode. Für C#/.NET-Erweiterungen ist ein passendes .NET SDK
erforderlich. Typische Ursachen für Buildfehler:

- SDK fehlt oder falsche Version,
- NuGet-Abhängigkeit nicht verfügbar,
- Syntax- oder Typfehler,
- doppelte Klassen oder Dateien,
- fehlende Assembly-Referenz.

## Paketierung

Die Paketierung erzeugt das Release-Artefakt. Ein Paket ist erst releasefähig, wenn Build,
Validierung, Paketprüfung und lokale Signatur erfolgreich abgeschlossen sind.

## Was nicht übersprungen werden darf

- Validierung vor Build und Paketierung,
- Signaturprüfung vor Marketplace-Upload,
- serverseitige Marketplace-Prüfung,
- AAIAS-/AAIAC-Zielprüfung bei Installation oder Laden.

## Fehlerhilfe

- [AAIA-Fehlerbehebung](../troubleshooting/index.md)
- vorhandene Quellartikel unter [`../help/troubleshooting/`](../help/troubleshooting/)
