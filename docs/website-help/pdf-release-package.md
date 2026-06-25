# PDF- und Release-Paket-Struktur

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, keine PDF-Generierung  
> Scope: Reihenfolge und Metadaten für spätere Ausgaben

Diese Datei legt fest, welche kanonischen Markdown-Seiten später in PDF- oder Release-Pakete
aufgenommen werden sollen. Sie erzeugt keine PDF-Dateien.

## Ausgabeziele

| Ausgabe | Zielgruppe | Quelle |
|---|---|---|
| Benutzerhandbuch | Anwender und neue Nutzer | `docs/user-manual/` |
| Entwicklerhandbuch | ETWs und Entwickler | `docs/developer-guide/` |
| Administratorhandbuch | Betreiber und Administratoren | `docs/admin-guide/` |
| Architekturauszug | Maintainer und technische Prüfer | `docs/architecture/` |
| Fehlerhilfe | Support, ETWs und Betreiber | `docs/troubleshooting/` |
| Glossar | alle Zielgruppen | `docs/glossary/` |

## Benutzerhandbuch-Reihenfolge

1. `docs/user-manual/00-was-ist-aaia.md`
2. `docs/user-manual/01-module-manager-einstieg.md`
3. `docs/user-manual/02-projekt-erstellen.md`
4. `docs/user-manual/03-validierung-build-paketierung.md`
5. `docs/user-manual/04-rollen-und-rechte.md`
6. `docs/user-manual/05-aaias-aaiaac-verbinden.md`
7. `docs/user-manual/10-sicherheit-und-laufzeitstatus.md`
8. `docs/troubleshooting/index.md`
9. `docs/glossary/index.md`

## Entwicklerhandbuch-Reihenfolge

1. `docs/developer-guide/00-was-ist-etw.md`
2. `docs/developer-guide/01-module-und-plugin-entwicklung.md`
3. `docs/developer-guide/03-validierung-build-paketierung.md`
4. `docs/developer-guide/04-manifest-und-permissions.md`
5. `docs/developer-guide/04-signatur-und-marketplace-release.md`
6. `docs/developer-guide/05-rollen-rechte-und-verantwortung.md`
7. `docs/developer-guide/06-signatur-und-trust-level.md`
8. `docs/developer-guide/07-marketplace-upload.md`
9. `docs/developer-guide/08-ki-handoff-und-connector.md`
10. `docs/developer-guide/09-fehleranalyse-und-diagnose.md`
11. `docs/developer-guide/10-sicherheit-und-laufzeitstatus.md`
12. `docs/developer-guide/10-air-runtime-und-tools.md`
13. `docs/developer-guide/runtime-durability.md`

## Administratorhandbuch-Reihenfolge

1. `docs/admin-guide/00-aaias-einrichten.md`
2. `docs/admin-guide/01-rollen-und-betriebsgrenzen.md`
3. `docs/admin-guide/02-persistenz-backup-recovery.md`
4. `docs/admin-guide/03-logs-audit-monitoring.md`
5. `docs/admin-guide/04-updates-und-release-betrieb.md`
6. `docs/admin-guide/10-runtime-betrieb-und-recovery.md`
7. `docs/troubleshooting/runtime-state-und-air.md`

## Release-Metadaten

Jede spätere Ausgabe braucht:

- Ausgabename,
- geprüfter Stand,
- Quell-Commit,
- Zielgruppe,
- Status der Quelle,
- Hinweis auf `DOCUMENTATION_TRUTH_RULE.md`,
- Ausschluss von Secrets, privaten Schlüsseln, Tokens und privaten Pfaden.

## Noch offen

- Layout und PDF-Template,
- automatische Inhaltsverzeichnisse,
- Versionierung im Website-Deployment,
- In-App-Bundling,
- CI-Linkprüfung.
