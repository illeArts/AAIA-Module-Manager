# Validierung, Build und Paketierung

> Zielgruppe: ETWs und Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: Kernpfad aus Help- und Developer-Doku konsolidiert

Vor einem Release müssen Struktur, Manifest, Build, Tests, Paketinhalt und Signatur
zusammenpassen. Der Module Manager kann lokal prüfen, aber nicht die serverseitige Prüfung
ersetzen.

## Reihenfolge

1. Manifest prüfen.
2. Permissions prüfen.
3. Build ausführen.
4. Tests ausführen.
5. Paketinhalt prüfen.
6. Signieren.
7. Signatur lokal verifizieren.
8. Marketplace-Upload starten.

## Validierung

Validierung blockiert bei fehlendem Manifest, ungültiger Version, unplausiblem Host,
verbotenen Dateien oder fehlenden Pflichtangaben. Warnungen dürfen nicht ignoriert werden,
wenn sie Trust, Sicherheit oder Marketplace-Fähigkeit betreffen.

## Build

Der Build muss reproduzierbar sein. Lokale Hilfsdateien, private Konfigurationen und
temporäre Artefakte gehören nicht in das Paket. Build-Fehler werden anhand ihrer echten
Fehlermeldung behoben, nicht durch Abschalten von Prüfungen.

## Tests

Mindestens prüfen:

- Manifest kann geladen werden,
- zentrale Entry-Points starten,
- Permissions passen zum Verhalten,
- keine Secrets oder privaten Pfade im Paket,
- bei AIR-Tools zusätzlich [Runtime Durability](runtime-durability.md).

## Paketprüfung

Ein Paket ist nur dann bereit für Signatur und Upload, wenn Inhalt, Manifest und Build-Artefakte
zusammenpassen. Das Paket bleibt ein lokales Artefakt, bis Marketplace oder Zielhost es
unabhängig akzeptieren.
