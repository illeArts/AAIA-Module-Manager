# Public Help Structure

> Vorgesehene Routen: `/handbuch`, `/docs`, `/help`  
> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, nicht deployed

Die Website-Hilfe darf keine eigene Wahrheitsschicht werden. Sie kuratiert Inhalte aus den
kanonischen Markdown-Quellen und verweist auf Version, Zielgruppe und Status.

## Routenmodell

| Route | Primäre Quelle | Ziel |
|---|---|---|
| `/handbuch` | `docs/user-manual/` | Anwender verstehen AAIA, Sicherheit und typische Nutzung |
| `/docs` | `docs/developer-guide/` und `docs/architecture/` | ETWs, Entwickler und technische Prüfer finden Entwicklungs- und Architekturwissen |
| `/help` | `docs/troubleshooting/` und `docs/glossary/` | Fehlerbehebung, Begriffe, FAQ und Support-Einstieg |

## Startkarten

| Karte | Zielroute | Quelle |
|---|---|---|
| Was ist AAIA? | `/handbuch/was-ist-aaia` | `user-manual/00-was-ist-aaia.md` |
| Module Manager Einstieg | `/handbuch/module-manager` | `user-manual/01-module-manager-einstieg.md` |
| Projekt erstellen | `/handbuch/projekt-erstellen` | `user-manual/02-projekt-erstellen.md` |
| Validierung, Build und Paketierung | `/handbuch/validierung-build-paketierung` | `user-manual/03-validierung-build-paketierung.md` |
| Rollen und Rechte | `/handbuch/rollen-und-rechte` | `user-manual/04-rollen-und-rechte.md` |
| AAIAS und AAIAC verbinden | `/handbuch/aaias-aaiaac-verbinden` | `user-manual/05-aaias-aaiaac-verbinden.md` |
| Sicherheit und Laufzeitstatus | `/handbuch/sicherheit-und-laufzeitstatus` | `user-manual/10-sicherheit-und-laufzeitstatus.md` |
| Entwicklerhandbuch | `/docs/developer` | `developer-guide/index.md` |
| Modul- und Plugin-Entwicklung | `/docs/module-und-plugin-entwicklung` | `developer-guide/01-module-und-plugin-entwicklung.md` |
| Manifest und Permissions | `/docs/manifest-und-permissions` | `developer-guide/04-manifest-und-permissions.md` |
| Validierung, Build und Paketierung | `/docs/validierung-build-paketierung` | `developer-guide/03-validierung-build-paketierung.md` |
| Release-Signatur vorbereiten | `/docs/release-signatur-vorbereiten` | `developer-guide/04-signatur-und-marketplace-release.md` |
| Rollen, Rechte und Verantwortung | `/docs/rollen-rechte-verantwortung` | `developer-guide/05-rollen-rechte-und-verantwortung.md` |
| Signatur und Trust-Level | `/docs/signatur-und-trust-level` | `developer-guide/06-signatur-und-trust-level.md` |
| Marketplace-Upload | `/docs/marketplace-upload` | `developer-guide/07-marketplace-upload.md` |
| KI-Handoff und Connector | `/docs/ki-handoff-connector` | `developer-guide/08-ki-handoff-und-connector.md` |
| Fehleranalyse und Diagnose | `/docs/fehleranalyse-diagnose` | `developer-guide/09-fehleranalyse-und-diagnose.md` |
| Sicherheit und Laufzeitstatus für Entwickler | `/docs/sicherheit-laufzeitstatus` | `developer-guide/10-sicherheit-und-laufzeitstatus.md` |
| AIR Runtime und Tools | `/docs/air-runtime-und-tools` | `developer-guide/10-air-runtime-und-tools.md` |
| Runtime Durability | `/docs/runtime-durability` | `developer-guide/runtime-durability.md` |
| Administratorhandbuch | `/docs/admin` | `admin-guide/index.md` |
| Admin-Rollen und Betriebsgrenzen | `/docs/admin/rollen-und-betriebsgrenzen` | `admin-guide/01-rollen-und-betriebsgrenzen.md` |
| Persistenz, Backup und Recovery | `/docs/admin/persistenz-backup-recovery` | `admin-guide/02-persistenz-backup-recovery.md` |
| Logs, Audit und Monitoring | `/docs/admin/logs-audit-monitoring` | `admin-guide/03-logs-audit-monitoring.md` |
| Updates und Release-Betrieb | `/docs/admin/updates-release-betrieb` | `admin-guide/04-updates-und-release-betrieb.md` |
| Runtime-Betrieb und Recovery | `/docs/admin/runtime-betrieb-und-recovery` | `admin-guide/10-runtime-betrieb-und-recovery.md` |
| AIR-State-Architektur | `/docs/air-state-architektur` | `architecture/air-runtime-state.md` |
| Runtime-State und AIR-Fehler | `/help/runtime-state-und-air` | `troubleshooting/runtime-state-und-air.md` |
| Glossar | `/help/glossar` | `glossary/index.md` |

## Veröffentlichungsregeln

- Jede öffentliche Seite zeigt geprüften Stand und Zielgruppe.
- Historische Begriffe werden sichtbar als historisch oder offen markiert.
- Nicht implementierte Produktpfade werden als geplant, vorbereitet oder spezifiziert markiert.
- Keine privaten Pfade, realen Zugangsdaten oder Secrets.
- Routing, Suche, In-App-Hilfe und PDF-Struktur sind in Phase 11.5.4 vorbereitet, aber nicht
  deployed oder generiert.

## Phase-11.5.4-Artefakte

- [Routing Map](routing-map.md)
- [Legacy-Aliase](legacy-aliases.json)
- [In-App-Hilfe-Mapping](in-app-help-map.md)
- [PDF- und Release-Paket-Struktur](pdf-release-package.md)
- [AAIAM Knowledge Sync](aaiam-knowledge-sync.md)
- [Export Pipeline Preparation](export-pipeline.md)
- Suchindex: [`../help/index.json`](../help/index.json)
- AAIAM-Importmap: [`../help/aaiam-import-map.json`](../help/aaiam-import-map.json)
- Error-Code-Referenz: [`../help/error-code-reference.md`](../help/error-code-reference.md)
- Exportmanifest: [`../export/export-manifest.json`](../export/export-manifest.json)
