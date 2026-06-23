# Validierung verstehen

## Kurz erklärt

Die Validierung ist eine automatische Prüfung, die AAIA durchführt bevor du deinen Code kompilierst. Sie stellt sicher, dass dein Projekt vollständig und korrekt aufgebaut ist.

Stell dir vor: AAIA schaut sich dein Projekt an und sagt dir, was fehlt oder falsch ist — bevor du stundenlang baust und dann feststellst, dass eine Pflichtdatei fehlt.

## Was prüft AAIA?

| Was | Warum |
|-----|-------|
| Manifest (`aaia-extension.json`) vorhanden | Pflichtdatei — ohne Manifest ist kein Modul erkennbar |
| Manifest-Inhalt gültig | Felder müssen korrekte Werte haben |
| Extension-ID eindeutig | Doppelte IDs führen zu Konflikten |
| Versionsnummer korrekt | Muss im Format `1.0.0` sein |
| Keine verbotenen Dateien | `.exe`, Passwörter, Private Keys sind verboten |
| Keine bekannten Sicherheitsprobleme | Schutz vor schädlichen Modulen |

## Was bedeuten Fehler, Warnungen und Hinweise?

### 🔴 Fehler (Blocker)

AAIA kann nicht weitermachen. Du musst das Problem beheben bevor der nächste Schritt möglich ist.

Beispiel: `aaia-extension.json fehlt` → du kannst nicht bauen, nicht paketieren, nicht hochladen.

### 🟡 Warnung

Es ist kein Blocker, aber du solltest es trotzdem beheben. Warnungen können später zu Fehlern werden oder beim Marketplace-Upload Probleme machen.

Beispiel: `Versionsnummer ungewöhnlich` → du kannst weitermachen, aber die Marketplace-Prüfung könnte es bemängeln.

### ℹ️ Hinweis

Eine Information, kein Problem. Nützlich zum Lesen, aber keine Aktion erforderlich.

## Was ist Auto-Fix?

Für manche Fehler kann AAIA die Lösung automatisch anwenden. Du siehst dann einen Button **„Automatisch reparieren"**. Klicke ihn an, und AAIA behebt das Problem.

> **Wichtig:** Auto-Fix schreibt nie automatisch — du musst ihn immer manuell bestätigen.

## Warum blockiert AAIA bestimmte Dinge?

AAIA schützt die Nutzer des Marketplaces. Wenn du zum Beispiel versuchst, eine `.exe`-Datei in das Paket aufzunehmen, wird AAIA das blockieren — weil ausführbare Dateien ein Sicherheitsrisiko darstellen könnten.

Das ist keine Einschränkung für dich als Entwickler, sondern ein Vertrauensversprechen an die Nutzer.

## Häufige Fehler bei der Validierung

- Manifest fehlt → `troubleshooting.manifest-missing`
- Manifest-JSON ungültig → Häufig ein fehlendes Komma oder eine fehlende geschweifte Klammer
- Doppelte Extension-ID → Name in `aaia-extension.json` muss weltweit eindeutig sein

## Technische Details

Die Validierung läuft über `ValidationService` und prüft sowohl Dateistruktur als auch Manifest-Semantik. Einige Prüfungen erfordern eine Internetverbindung (Extension-ID-Einzigartigkeit), andere laufen vollständig lokal.

## Verwandte Themen

- Manifest-Datei fehlt (Fehlerartikel)
- Build verstehen
