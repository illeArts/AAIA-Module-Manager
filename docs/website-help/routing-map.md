# Public Help Routing Map

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, nicht deployed  
> Scope: Website-Routen für kanonische Markdown-Quellen

Diese Datei definiert stabile öffentliche Zielrouten. Sie ist eine Routing-Vorbereitung,
kein Website-Deployment.

## Grundrouten

| Route | Quelle | Zweck |
|---|---|---|
| `/handbuch` | `docs/user-manual/index.md` | öffentlicher Anwender-Einstieg |
| `/docs` | `docs/developer-guide/index.md` und `docs/architecture/index.md` | Entwickler- und Architekturwissen |
| `/help` | `docs/troubleshooting/index.md` und `docs/glossary/index.md` | Fehlerhilfe, Glossar und Suche |

## Handbuch-Routen

| Route | Quelle | Status |
|---|---|---|
| `/handbuch/was-ist-aaia` | `docs/user-manual/00-was-ist-aaia.md` | kanonisch |
| `/handbuch/module-manager` | `docs/user-manual/01-module-manager-einstieg.md` | kanonisch |
| `/handbuch/projekt-erstellen` | `docs/user-manual/02-projekt-erstellen.md` | kanonisch |
| `/handbuch/validierung-build-paketierung` | `docs/user-manual/03-validierung-build-paketierung.md` | kanonisch |
| `/handbuch/rollen-und-rechte` | `docs/user-manual/04-rollen-und-rechte.md` | kanonisch |
| `/handbuch/aaias-aaiaac-verbinden` | `docs/user-manual/05-aaias-aaiaac-verbinden.md` | kanonisch |
| `/handbuch/sicherheit-und-laufzeitstatus` | `docs/user-manual/10-sicherheit-und-laufzeitstatus.md` | kanonisch |

## Entwickler- und Architektur-Routen

| Route | Quelle | Status |
|---|---|---|
| `/docs/developer` | `docs/developer-guide/index.md` | kanonisch |
| `/docs/module-und-plugin-entwicklung` | `docs/developer-guide/01-module-und-plugin-entwicklung.md` | kanonisch |
| `/docs/validierung-build-paketierung` | `docs/developer-guide/03-validierung-build-paketierung.md` | kanonisch |
| `/docs/manifest-und-permissions` | `docs/developer-guide/04-manifest-und-permissions.md` | kanonisch |
| `/docs/release-signatur-vorbereiten` | `docs/developer-guide/04-signatur-und-marketplace-release.md` | kanonisch |
| `/docs/rollen-rechte-verantwortung` | `docs/developer-guide/05-rollen-rechte-und-verantwortung.md` | kanonisch |
| `/docs/signatur-und-trust-level` | `docs/developer-guide/06-signatur-und-trust-level.md` | kanonisch |
| `/docs/marketplace-upload` | `docs/developer-guide/07-marketplace-upload.md` | kanonisch |
| `/docs/ki-handoff-connector` | `docs/developer-guide/08-ki-handoff-und-connector.md` | kanonisch |
| `/docs/fehleranalyse-diagnose` | `docs/developer-guide/09-fehleranalyse-und-diagnose.md` | kanonisch |
| `/docs/sicherheit-laufzeitstatus` | `docs/developer-guide/10-sicherheit-und-laufzeitstatus.md` | kanonisch |
| `/docs/air-runtime-und-tools` | `docs/developer-guide/10-air-runtime-und-tools.md` | kanonisch |
| `/docs/runtime-durability` | `docs/developer-guide/runtime-durability.md` | kanonisch |
| `/docs/architecture` | `docs/architecture/index.md` | kanonisch |
| `/docs/air-state-architektur` | `docs/architecture/air-runtime-state.md` | kanonisch |

## Admin-Routen

| Route | Quelle | Status |
|---|---|---|
| `/docs/admin` | `docs/admin-guide/index.md` | kanonisch |
| `/docs/admin/aaias-einrichten` | `docs/admin-guide/00-aaias-einrichten.md` | kanonisch |
| `/docs/admin/rollen-und-betriebsgrenzen` | `docs/admin-guide/01-rollen-und-betriebsgrenzen.md` | kanonisch |
| `/docs/admin/persistenz-backup-recovery` | `docs/admin-guide/02-persistenz-backup-recovery.md` | kanonisch |
| `/docs/admin/logs-audit-monitoring` | `docs/admin-guide/03-logs-audit-monitoring.md` | kanonisch |
| `/docs/admin/updates-release-betrieb` | `docs/admin-guide/04-updates-und-release-betrieb.md` | kanonisch |
| `/docs/admin/runtime-betrieb-und-recovery` | `docs/admin-guide/10-runtime-betrieb-und-recovery.md` | kanonisch |

## Help-Routen

| Route | Quelle | Status |
|---|---|---|
| `/help/troubleshooting` | `docs/troubleshooting/index.md` | kanonisch |
| `/help/runtime-state-und-air` | `docs/troubleshooting/runtime-state-und-air.md` | kanonisch |
| `/help/glossar` | `docs/glossary/index.md` | kanonisch |
| `/help/begriffstatus` | `docs/glossary/term-status.md` | kanonisch für Dokumentationsprüfung |

## Veröffentlichungsregel

Die Website darf aus dieser Datei Routen ableiten. Sie darf Inhalte aber nicht verändern oder
als implementiert markieren, wenn die kanonische Quelle nur „geplant“, „vorbereitet“,
„spezifiziert“ oder „historisch“ sagt.
