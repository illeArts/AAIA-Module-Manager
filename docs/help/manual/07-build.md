# Was bedeutet Build?

## Kurz erklärt

Der **Build** ist der Schritt, bei dem dein Quellcode in ein lauffähiges Programm umgewandelt wird. Computer verstehen keinen menschlichen Code direkt — der Code muss erst „übersetzt" (kompiliert) werden.

**Vereinfacht:** Build = dein Code wird in eine `.dll`-Datei umgewandelt, die AAIAS ausführen kann.

## Was ist .NET?

**.NET** ist die technische Grundlage, auf der AAIA-Module laufen. Es ist eine von Microsoft entwickelte Software-Plattform. Dein Modul wird in der Sprache **C#** geschrieben und mit dem **.NET 8 SDK** kompiliert.

> **Was du tun musst:** Das .NET 8 SDK einmalig installieren. AAIA übernimmt den Rest.

**Download:** [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

## Warum schlägt der Build manchmal fehl?

Häufigste Ursachen:

| Ursache | Erkennungszeichen |
|---------|------------------|
| .NET SDK fehlt oder falsche Version | „dotnet not found" oder Version-Mismatch |
| NuGet-Paket nicht verfügbar | Kein Internetzugang, Paket existiert nicht mehr |
| Tippfehler im Code | Fehlermeldung mit Dateiname + Zeilennummer |
| Datei doppelt vorhanden | „Duplicate class" oder „Ambiguous reference" |
| Fehlende Abhängigkeit | „Could not find assembly" |

## Schritt für Schritt: Build starten

1. Öffne den Tab **„Build"** im Module Manager.
2. Klicke auf **„Build starten"**.
3. AAIA zeigt dir in Echtzeit die Ausgabe.
4. Bei Fehlern erscheinen sie rot und klickbar — ein Klick öffnet den passenden Hilfeartikel.

## Was bedeuten die Build-Ausgaben?

```
Build succeeded.    → Alles OK ✅
Build FAILED.       → Fehler vorhanden ❌
warning CS...       → Warnung (kein Blocker, aber beachten)
error CS...         → Fehler (muss behoben werden)
```

## Auto-Fix für Build-Fehler

AAIA kann bestimmte Build-Fehler automatisch beheben, zum Beispiel:
- Fehlende NuGet-Pakete installieren
- Offensichtliche Namespace-Fehler korrigieren

Andere Fehler musst du manuell im Code beheben.

## Technische Details

AAIA ruft intern `dotnet build` mit den passenden Parametern auf. Die Ausgabe wird in Echtzeit geparst und nach Fehler-Codes durchsucht. AAIA versucht, technische .NET-Fehlercodes in verständliche Erklärungen zu übersetzen.

Für fortgeschrittene Nutzer: Die Build-Parameter sind in `BuildSettings` konfigurierbar.

## Verwandte Themen

- Build fehlgeschlagen (Fehlerartikel)
- .NET SDK nicht gefunden (Fehlerartikel)
- Validierung verstehen
