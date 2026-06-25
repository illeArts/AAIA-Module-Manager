# In-App-Hilfe-Mapping

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, nicht implementiert  
> Scope: Module-Manager-Flows zu kanonischen Hilfeseiten

Dieses Mapping beschreibt, welche Hilfeseite aus UI-Bereichen oder Pipeline-Schritten geöffnet
werden soll. Es implementiert keine In-App-Hilfe.

| App-Bereich / Flow | Zielroute | Kanonische Quelle |
|---|---|---|
| Start / Willkommen | `/handbuch/was-ist-aaia` | `docs/user-manual/00-was-ist-aaia.md` |
| Module Manager Übersicht | `/handbuch/module-manager` | `docs/user-manual/01-module-manager-einstieg.md` |
| Projekt-Wizard / Idee | `/handbuch/projekt-erstellen` | `docs/user-manual/02-projekt-erstellen.md` |
| Projekttyp / Modul oder Plugin | `/docs/module-und-plugin-entwicklung` | `docs/developer-guide/01-module-und-plugin-entwicklung.md` |
| Manifest-Editor | `/docs/manifest-und-permissions` | `docs/developer-guide/04-manifest-und-permissions.md` |
| Validierung | `/docs/validierung-build-paketierung` | `docs/developer-guide/03-validierung-build-paketierung.md` |
| Build | `/docs/validierung-build-paketierung` | `docs/developer-guide/03-validierung-build-paketierung.md` |
| Paketprüfung | `/docs/validierung-build-paketierung` | `docs/developer-guide/03-validierung-build-paketierung.md` |
| Signatur | `/docs/signatur-und-trust-level` | `docs/developer-guide/06-signatur-und-trust-level.md` |
| Release-Vorbereitung | `/docs/release-signatur-vorbereiten` | `docs/developer-guide/04-signatur-und-marketplace-release.md` |
| Marketplace | `/docs/marketplace-upload` | `docs/developer-guide/07-marketplace-upload.md` |
| Connector / KI-Handoff | `/docs/ki-handoff-connector` | `docs/developer-guide/08-ki-handoff-und-connector.md` |
| Fehlerdetails | `/docs/fehleranalyse-diagnose` | `docs/developer-guide/09-fehleranalyse-und-diagnose.md` |
| AIR Runtime | `/docs/air-runtime-und-tools` | `docs/developer-guide/10-air-runtime-und-tools.md` |
| Runtime-State / Recovery | `/help/runtime-state-und-air` | `docs/troubleshooting/runtime-state-und-air.md` |
| Sicherheit | `/docs/sicherheit-laufzeitstatus` | `docs/developer-guide/10-sicherheit-und-laufzeitstatus.md` |

## Sicherheitsregel

Kontextsensitive Hilfe darf Fehlermeldungen, Reason-Codes und redigierte Diagnose anzeigen.
Sie darf keine Tokens, privaten Schlüssel, vollständigen Konfigurationen oder privaten Pfade in
eine externe Route oder KI-Übergabe übernehmen.
