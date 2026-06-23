# Ein neues Projekt erstellen

## Kurz erklärt

Im AAIA Module Manager startest du ein neues Modul immer mit dem **Projekt-Wizard**. Du gibst eine Idee ein, die KI analysiert sie und AAIA legt die Projektstruktur automatisch an.

## Wann brauche ich das?

Immer wenn du ein komplett neues Modul entwickeln möchtest.

## Schritt für Schritt

### 1. Neue Idee eingeben

Klicke auf **„Neue Idee"** oder **„Neues Projekt"**. Beschreibe kurz, was dein Modul tun soll.

**Tipp:** Je genauer du die Idee beschreibst, desto besser kann AAIA den Projekttyp einschätzen.

Beispiele für gute Beschreibungen:
- „Ein Modul, das automatisch Textzusammenfassungen erstellt"
- „Ein Datenexport-Modul für CSV-Dateien"
- „Eine Sprachsteuerung für AAIAS-Befehle"

### 2. KI-Analyse verstehen

AAIA analysiert deine Idee und schlägt vor:
- Welcher **Projekttyp** am besten passt
- Welche **Abhängigkeiten** du wahrscheinlich benötigst
- Einen **vorgeschlagenen Namen** für das Modul

> **Wichtig:** Die KI-Analyse ist ein Vorschlag, keine Entscheidung. Du kannst alles anpassen.

### 3. Projekttyp auswählen

AAIA kennt verschiedene Projekttypen. Wähle den, der am besten zu deinem Vorhaben passt. Wenn du unsicher bist, nimm **Standard Extension** — du kannst später wechseln.

### 4. Projekt anlegen lassen

Klicke auf **„Projekt erstellen"**. AAIA legt automatisch folgende Struktur an:

```
MeinModul/
  src/
    MeinModul/
      MeinModul.csproj
      ExtensionEntry.cs
      aaia-extension.json
  README.md
```

### 5. Projektstruktur verstehen

- `MeinModul.csproj` — die Projektdatei (.NET)
- `ExtensionEntry.cs` — der Einstiegspunkt des Moduls
- `aaia-extension.json` — das **Manifest** (Pflichtdatei)

Das Manifest beschreibt dein Modul: Name, Version, ID, Lizenztyp und mehr.

## Häufige Fragen

**Kann ich den Namen nachträglich ändern?**  
Ja, aber du musst dabei das Manifest und die Projektdatei anpassen. AAIA hilft dabei.

**Was wenn die KI-Analyse falsch liegt?**  
Das passiert. Du kannst alle Vorschläge der KI ignorieren und manuell einrichten.

## Technische Details

Das Manifest (`aaia-extension.json`) ist die wichtigste Datei. Ohne gültiges Manifest kann AAIA dein Projekt nicht validieren. Pflichfelder sind: `extensionId`, `version`, `displayName`, `licenseType`.

## Verwandte Themen

- Validierung verstehen
- Build verstehen
