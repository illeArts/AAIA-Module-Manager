# Build-Fehler & .NET SDK

## Problem: Build fehlgeschlagen

### Was bedeutet das?

AAIA hat versucht, deinen Quellcode zu kompilieren, aber es ist ein Fehler aufgetreten. Das Modul kann erst fertiggestellt werden, wenn der Build erfolgreich ist.

### Warum passiert das?

Die häufigsten Ursachen:

**1. .NET 8 SDK nicht installiert oder falsche Version**

> AAIA Module Manager benötigt das **.NET 8 SDK**.

Lösung:
1. Öffne ein Terminal (Windows: `cmd` oder `PowerShell`)
2. Tippe: `dotnet --version`
3. Wenn du `8.x.x` siehst, ist das SDK vorhanden
4. Wenn du `command not found` oder eine andere Version siehst, installiere .NET 8:
   [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

---

**2. Manifest-Datei fehlt (`aaia-extension.json`)**

### Was bedeutet das?

Die Datei `aaia-extension.json` wurde nicht gefunden. Diese Datei ist das Herzstück deines Moduls — ohne sie weiß AAIA nicht, was dein Modul ist.

### Lösung

1. Prüfe ob die Datei im Projektordner liegt (direkt in `src/DeinModul/`)
2. Prüfe ob der Name exakt `aaia-extension.json` lautet (Groß-/Kleinschreibung!)
3. Falls sie fehlt: Klicke auf **„Manifest neu erstellen"** in AAIA

**Minimales gültiges Manifest:**
```json
{
  "extensionId": "dein.modul.id",
  "version": "1.0.0",
  "displayName": "Dein Modul",
  "licenseType": "Free"
}
```

---

**3. NuGet-Paket nicht verfügbar**

NuGet ist das Paketsystem für .NET. Wenn ein Paket nicht heruntergeladen werden kann:

1. Prüfe deine Internetverbindung
2. Versuche: `dotnet restore` im Terminal
3. Prüfe ob das Paket noch existiert (Paketnamen bei [nuget.org](https://nuget.org) suchen)

---

**4. Tippfehler oder Syntaxfehler im Code**

Die Fehlermeldung enthält immer eine Datei und eine Zeilennummer, z.B.:

```
error CS1002: ; expected [MeinModul.cs(42,15)]
```

Das bedeutet: In Zeile 42, Spalte 15 der Datei `MeinModul.cs` fehlt ein Semikolon.

Öffne die Datei in deinem Editor und behebe den Fehler.

---

**5. Doppelte Klasse oder doppelter Namespace**

```
error CS0101: The namespace '...' already contains a definition for '...'
```

Das passiert wenn zwei Dateien eine gleichnamige Klasse enthalten. Lösung: Eine der Klassen umbenennen oder in einen anderen Namespace verschieben.

---

## Schritt-für-Schritt-Diagnose

1. Lies die Fehlermeldung genau — sie enthält Datei + Zeile
2. Suche in AAIA nach dem Fehlercode (z.B. `CS1002`)
3. Klicke auf **„Hilfe öffnen"** neben dem Fehler
4. Falls du nicht weiterkommst: Nutze den **KI-Hilfe-Tab** um einen Kontext zu erzeugen und deine KI um Rat zu fragen

## Verwandte Themen

- Was bedeutet Build?
- Validierung verstehen
