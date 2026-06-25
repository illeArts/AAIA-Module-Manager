# Modul- und Plugin-Entwicklung

> Zielgruppe: ETWs und Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: migriert aus `aaia-developer-docs`; Host- und Trust-Grenzen geprüft

AAIA unterscheidet serverseitige Module für AAIAS und clientseitige Plugins für AAIAC.
Beide verwenden ein Manifest, Build-Artefakte und explizite Permissions, haben aber
unterschiedliche Zielhosts.

## Modul oder Plugin?

| Typ | Zielhost | Zweck |
|---|---|---|
| Modul | `AAIAS` | serverseitige APIs, Services und Automatisierung |
| Plugin | `AAIAC` | clientseitige Oberfläche, Ereignisse und Client-Integration |

Ein Modul darf nicht still als Client-Plugin dokumentiert werden und umgekehrt. Hybrid-Pfade
brauchen eigene Architektur- und Sicherheitsprüfung.

## Servermodul

Ein serverseitiges Modul ist typischerweise eine C#/.NET-Bibliothek mit eindeutigem Modul-ID,
Assembly und Manifest. Routen liegen unter einem modulbezogenen Prefix, z. B. sinngemäß
`/api/modules/{modul-id}/`.

Grundregeln:

- keine direkten Datenbankzugriffe ohne deklarierte Berechtigung,
- keine Shell-Ausführung ohne explizite Permission,
- keine globalen API-Routen außerhalb des Modulbereichs,
- Version im Code und Manifest konsistent halten.

## Client-Plugin

Ein Client-Plugin zielt auf `AAIAC`. Es erweitert UI oder Client-Verhalten und darf
serverseitige Entscheidungen nicht lokal simulieren.

Grundregeln:

- Client-Plugin mit `host: "AAIAC"` deklarieren,
- benötigte Permissions explizit aufführen,
- keine Secrets in Plugin-Ressourcen ablegen,
- Serverkommunikation über vorgesehene Schnittstellen führen.

## Projektstart

1. Zielhost festlegen.
2. ID, Name, Version und Assembly-Namen wählen.
3. Manifest anlegen.
4. Permissions minimal deklarieren.
5. Build und Validierung lokal ausführen.
6. Paket signieren und prüfen.
7. Marketplace-Upload erst nach lokaler Verifikation starten.

## Quellenabgleich

Diese Seite übernimmt die tragfähigen Konzepte aus `aaia-developer-docs`:
`module-development.md`, `plugin-development.md` und `conventions.md`. Aussagen zu konkreten
Deployment-Pfaden wurden nicht als öffentliche Produktzusage übernommen.
