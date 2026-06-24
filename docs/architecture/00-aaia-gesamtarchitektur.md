# AAIA-Gesamtarchitektur

> Zielgruppe: Maintainer, Integrationsentwickler und technische Prüfer

AAIA ist kein einzelnes Programm. Die Plattform trennt Entwicklungswerkzeug, Server, Client,
Marketplace und Intelligence Runtime durch explizite Vertrauens- und Integrationsgrenzen.

```text
ETW / Administrator / Anwender
              |
              v
     AAIA Module Manager ------> Marketplace
              |                       |
              v                       v
            AAIAS <-------------- .aaiaext
              |
              +------ AAIAC / weitere Hosts

AIR Contracts <- AIR Core <- AIR Adapter (z. B. MCP)
     ^               ^
     |               |
Module Manager, AAIAS und weitere Apps sind Hosts/Nutzer
```

## Harte Grenzen

- AIR kennt keine konkrete App; Apps integrieren AIR über Contracts und Host-Interfaces.
- Der Scheduler entscheidet **wer und wann**, der Resource Manager **wo Kapazität reserviert wird**.
- Runtime-Permissions und Tool-Ausführung bleiben eine getrennte Sicherheitskette.
- Ein lokaler Client darf keine serverseitige Marketplace-Vertrauensstufe setzen.
- AAIAS prüft ein Paket selbst; vorgelagerte Prüfung ersetzt diese Grenze nicht.
- Dokumentationsausgaben dürfen keine Secrets oder privaten Betriebsdetails enthalten.

## Quellen

- [AIR-Plattform-Split](../air/air-platform-split.md)
- [Phase 8 Orchestration](../phase-8-ai-orchestration.md)
- [Phase 9 Durable Runtime State](../phase-9-durable-runtime-state-spec.md)
- [Phase 10 Production Hardening](../phase-10-production-hardening-spec.md)

Die API-, Datenbank- und Deployment-Topologie wird erst nach Abgleich mit den jeweiligen
Produkt-Repositories als verbindliche Gesamtarchitektur ergänzt.
