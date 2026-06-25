# Phase 11.5.2 — Dokumentationsinventar

> Geprüfter Stand: 2026-06-25  
> Regel: [DOCUMENTATION_TRUTH_RULE.md](DOCUMENTATION_TRUTH_RULE.md)  
> Scope: vorhandene Dokumentation im Repository `aaia-module-manager`

Dieses Inventar trennt kanonische Zielbereiche, migrierte Inhalte und historische Quellen.
Es ist kein Produktversprechen. Jede Aussage bleibt nur dann öffentlich gültig, wenn sie durch
aktuellen Code, Tests, freigegebene Spezifikation oder eine ausdrücklich markierte historische
Quelle belegt ist.

## Zielstruktur

| Zielbereich | Status nach 11.5.2 | Zweck |
|---|---|---|
| `user-manual/` | erweitert | öffentlicher Anwender-Einstieg, Sicherheit und Laufzeitstatus |
| `developer-guide/` | erweitert | ETW-/AIR-Entwicklerpfade, Runtime-Grenzen und Tool-Regeln |
| `admin-guide/` | erweitert | Runtime-Betrieb, Protectoren, Recovery, Backpressure und Rollback |
| `architecture/` | erweitert | AIR-State-, Lifecycle- und Persistenzgrenzen |
| `troubleshooting/` | erweitert | strukturierter Einstieg plus Runtime-/AIR-Fehlerbilder |
| `website-help/` | erweitert | öffentliche Routen, Inhaltsquellen und Vorbereitungsstatus |
| `glossary/` | geprüft | Begriffstatus bleibt verbindlich; ETW-Langform weiterhin offen |
| `phases/` | erweitert | Abschlussnachweis für 11.5.2 |

## Quelleninventar

| Quelle | Art | Migrationsentscheidung | Statusregel |
|---|---|---|---|
| `docs/help/manual/` | bisherige öffentliche Hilfe | als lesbare Hilfequelle erhalten; Inhalte werden zielgerichtet in `user-manual/`, `developer-guide/` und `admin-guide/` übernommen | einzelne Aussagen müssen gegen Glossar und aktuelle Specs geprüft werden |
| `docs/help/troubleshooting/` | bisherige Fehlerartikel | bleibt als Quellbestand; zentrale Struktur liegt unter `troubleshooting/` | Fehlerbilder dürfen keine Sicherheitsprüfungen deaktivieren |
| `docs/air/` | technische AIR-Handoffs und Architektur | bleibt technische Quelle; relevante Betriebsregeln werden in Admin-, Entwickler- und Architekturtexte übernommen | Handoff ist kein alleiniger Implementierungsnachweis |
| `docs/phase-*-spec.md` | Spezifikationen | bleiben als freigegebene technische Quellen mit jeweiligem Status | Spezifikation bedeutet nicht automatisch öffentlich implementiert |
| `docs/phases/*-final.md` | Abschlussnachweise | bleiben KI-/Maintainer-Kontext und Migrationsquelle | geprüfter Stand und bekannte Grenzen beachten |
| `docs/glossary/` | Terminologie | kanonisch für Begriffe und Status | unbestätigte Langformen nicht erfinden |
| `README.md` | Projekt-Einstieg und Build-Hinweise | bleibt Einstieg, nicht vollständiges Handbuch | Plattform- und Release-Aussagen gegen aktuelle Quellen prüfen |
| `docs/WINDOWS_AUTH_TOTP_RUNBOOK.md` | internes Runbook | nicht öffentlich migriert; nur redigierte, allgemeine Fehlerbilder dürfen übernommen werden | keine privaten Pfade, Tokens oder realen Betriebsdaten kopieren |
| extern: `aaia-developer-docs` | separates Dokumentationsrepo | in 11.5.3 für Modul-, Plugin-, Manifest-, Permission- und Konventionsgrundlagen abgeglichen | nicht übernommene Deployment-Details bleiben externe Quelle |

## Migrierte Phase-10-Betriebsinhalte

Phase 10 ist technisch abgeschlossen und wird ab 11.5.2 dokumentarisch nutzbar gemacht:

- typed Delta-Writer und Phase-9-Checkpoint-Migration,
- gemischtes Recovery aus altem Checkpoint und neuen Deltas,
- Rollback-Schalter auf Phase-9-Writer,
- native Protectoren für Windows, macOS und Linux mit fail-closed Verhalten,
- app-neutraler Runtime-Lifecycle mit Readiness-Lease,
- geordneter Shutdown, Backpressure und Crash-/Restart-Konformität,
- idempotente externe Tool-Seiteneffekte über Operation-IDs.

Die öffentliche Hilfe beschreibt diese Punkte zielgruppengerecht. Interne Dateinamen,
Benutzerpfade und Schlüsselmaterial bleiben ausgeschlossen.

## Neue kanonische 11.5.2-Seiten

| Seite | Zweck |
|---|---|
| `troubleshooting/runtime-state-und-air.md` | Fehlerbilder, Reason-Codes, Entscheidungsbaum und sichere Eskalation |
| `architecture/air-runtime-state.md` | AIR-State-Komponenten, Datenfluss, Migration und Trust-Grenzen |
| `developer-guide/runtime-durability.md` | Operation-ID-, Delta-, Snapshot-, Recovery- und Testregeln für Entwickler |
| `admin-guide/10-runtime-betrieb-und-recovery.md` | Betreiberwissen zu Aktivierung, Rollback, Protectoren und Shutdown |

## Neue kanonische 11.5.3-Seiten

| Seite | Zweck |
|---|---|
| `user-manual/01-module-manager-einstieg.md` | Module-Manager-Rolle, lokale/serverseitige Grenzen |
| `user-manual/02-projekt-erstellen.md` | Projektstart und sichere KI-/Wizard-Nutzung |
| `user-manual/03-validierung-build-paketierung.md` | lokale Vorprüfung bis Paketierung |
| `user-manual/04-rollen-und-rechte.md` | Rollen und Trust-Stufen |
| `user-manual/05-aaias-aaiaac-verbinden.md` | sicherer Verbindungsrahmen für Zielhosts |
| `developer-guide/01-module-und-plugin-entwicklung.md` | AAIAS-Module, AAIAC-Plugins und Host-Grenzen |
| `developer-guide/03-validierung-build-paketierung.md` | Entwicklungsprüfung vor Release |
| `developer-guide/04-manifest-und-permissions.md` | Manifestfelder und Permission-Regeln |
| `developer-guide/04-signatur-und-marketplace-release.md` | Workflow zur Release-Signatur; Details liegen in Trust-Level- und Marketplace-Seiten |
| `developer-guide/05-rollen-rechte-und-verantwortung.md` | Entwicklerrollen, Rechte, Release-Verantwortung und Permission-Verantwortung |
| `developer-guide/06-signatur-und-trust-level.md` | Trust-Level-Hierarchie, lokale Signatur und Marketplace-Grenze |
| `developer-guide/07-marketplace-upload.md` | Upload-Voraussetzungen, Status und Blocker |
| `developer-guide/08-ki-handoff-und-connector.md` | sichere KI-Handoffs, Connector-Grenzen und AIR/MCP-Kontext |
| `developer-guide/09-fehleranalyse-und-diagnose.md` | reproduzierbare und sichere Fehleranalyse für Entwickler |
| `developer-guide/10-sicherheit-und-laufzeitstatus.md` | Entwicklerpflichten für Sicherheit, Runtime-State und fail-closed Verhalten |
| `admin-guide/01-rollen-und-betriebsgrenzen.md` | Betriebsrollen und Admin-Grenzen |
| `admin-guide/02-persistenz-backup-recovery.md` | Backup- und Recovery-Rahmen |
| `admin-guide/03-logs-audit-monitoring.md` | sichere Betriebsdiagnostik |
| `admin-guide/04-updates-und-release-betrieb.md` | Update- und Rollback-Rahmen |

## Neue 11.5.4-Ausgabeartefakte

| Artefakt | Zweck |
|---|---|
| `website-help/routing-map.md` | stabile öffentliche Routen für `/handbuch`, `/docs` und `/help` |
| `website-help/legacy-aliases.json` | Weiterleitungs-/Aliasplanung für alte Help-Pfade |
| `help/index.json` | vorbereiteter Suchindex mit kanonischem/legacy Status |
| `website-help/in-app-help-map.md` | Mapping von Module-Manager-Flows auf Hilfeseiten |
| `website-help/pdf-release-package.md` | Reihenfolge und Metadaten für spätere PDF-/Release-Ausgaben |

## Geplante 11.5.5-AAIAM-Artefakte

| Artefakt | Zweck |
|---|---|
| `website-help/aaiam-knowledge-sync.md` | AAIAM als späterer Hilfe-/Wissensspeicher, Importfluss und Sicherheitsgrenzen |
| `help/aaiam-import-map.json` | vorbereiteter Importvertrag für kanonische und historische Quellen |
| `help/error-code-reference.md` | referenzierbare Fehler-/Reason-Code-Basis für Help, In-App und AAIAM |
| `help/in-app-context-map.json` | maschinenlesbare Hilfe-Kontexte für Module Manager, AAIAS/AAIAC und AAIAM |

AAIAM wird nicht als kanonische Quelle geführt. Importierte Inhalte müssen auf Markdown-Quelle,
Version, Status, Zielgruppe und Redaktionsstatus verweisen. Historische Quellen erhalten nur
nach Prüfung `importAllowed: true`; Rohtexte bleiben gesperrt.

## Offene Migrationen

- öffentliche Installationsmatrix für AAIAS/AAIAC nach Produktfreigabe,
- Website-Deployment,
- tatsächliche Suchimplementierung,
- produktive AAIAM-DB-Befüllung,
- AAIAM-Bibliotheksintegration,
- PDF-Generierung und In-App-Ausgabe,
- automatische Linkprüfung als Build-Schritt.
