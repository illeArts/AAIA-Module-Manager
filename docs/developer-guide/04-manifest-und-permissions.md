# Manifest und Permissions

> Zielgruppe: ETWs und Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: migriert aus externer Developer-Doku und aktueller Help-Struktur

Jede AAIA-Erweiterung braucht ein Manifest. Das Manifest beschreibt Identität, Zielhost,
Assembly, Version, Plattformen und angeforderte Berechtigungen.

## Pflichtfelder

| Feld | Zweck |
|---|---|
| `id` | global eindeutige Erweiterungs-ID, typischerweise Reverse-Domain |
| `displayName` | lesbarer Name |
| `version` | SemVer-Version |
| `host` | Zielhost, z. B. `AAIAS` oder `AAIAC` |
| `kind` | Erweiterungstyp, z. B. `Module` oder `Plugin` |
| `assembly` | DLL-Dateiname |
| `permissions` | explizit angeforderte Berechtigungen |
| `supportedPlatforms` | Zielplattformen oder `all` |

Optionale Felder wie Beschreibung, Autor, Tags oder Mindest-Host-Version dürfen ergänzt
werden, wenn sie durch Produktstand und Release-Pfad gedeckt sind.

## Beispiel

```json
{
  "id": "com.example.my-extension",
  "displayName": "My Extension",
  "version": "1.0.0",
  "host": "AAIAS",
  "kind": "Module",
  "assembly": "AAIAS.Module.MyExtension.dll",
  "permissions": [],
  "supportedPlatforms": ["all"]
}
```

Beispielwerte sind fiktiv. Keine echten Servernamen, Tokens, Pfade oder Schlüssel in Manifest-
Beispiele aufnehmen.

## Permission-Regeln

Permissions müssen minimal, konkret und nachvollziehbar sein.

| Scope | Bedeutung | Dokumentationsregel |
|---|---|---|
| `network.outbound` | ausgehende Netzwerkverbindungen | Zielbereiche konkret beschreiben |
| `filesystem.read` | lokale Dateien lesen | keine privaten Benutzerpfade dokumentieren |
| `filesystem.write` | lokale Dateien schreiben | nur notwendige Zielbereiche |
| `shell.exec` | Shell-Befehle ausführen | nur mit starker Begründung |
| `database.read` | Datenbank lesen | Tabellen/Scopes begründen |
| `database.write` | Datenbank schreiben | besonders restriktiv prüfen |

Breite Berechtigungen wie vollständiger Dateisystemzugriff sind nicht releasefähig, solange sie
nicht fachlich und sicherheitlich begründet sind.

## Validierungsregel

Eine Erweiterung darf keine nicht deklarierten Fähigkeiten verwenden. Zielhosts dürfen Pakete
ablehnen, wenn Manifest, Assembly, Version oder Permissions nicht zusammenpassen.
