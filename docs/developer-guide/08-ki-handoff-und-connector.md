# KI-Handoff und Connector

> Zielgruppe: ETWs, Integrationsentwickler und Maintainer  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerhandbuch-Ergänzung aus AI-Handoff- und AIR-Handoff-Quellen

Der Module Manager kann KI-Kontexte und Connector-Flüsse unterstützen. Diese Funktionen
beschleunigen Entwicklung und Analyse, dürfen aber keine Sicherheits- oder Schreibgrenzen
umgehen.

## Grundprinzip

KI-Unterstützung arbeitet mit kontrollierten Kontexten:

- Projektinformationen werden reduziert und zielgerichtet übergeben.
- Secrets, Tokens, private Schlüssel und private Pfade werden nicht übernommen.
- Änderungen werden als Vorschlag oder Patch sichtbar gemacht.
- Schreibende Aktionen benötigen Bestätigung.
- Terminal- und Tool-Aktionen bleiben allowlist- und hostgebunden.

## Handoff-Inhalte

Ein guter KI-Handoff enthält:

- Produkt- und Commit-/Release-Stand,
- Ziel der Aufgabe,
- relevante Architektur- und Trust-Grenzen,
- betroffene Dateien oder Komponenten,
- aktuelle Fehlercodes oder Logs, redigiert,
- klare Nicht-Ziele.

Ein Handoff enthält nicht:

- private Schlüssel,
- Marketplace-Tokens,
- `.env`-Inhalte,
- echte Serverzugänge,
- vollständige private Benutzerpfade,
- unbestätigte Produktversprechen.

## Connector-Grenzen

Connectoren dürfen Entwicklung unterstützen, aber nicht eigenmächtig produktive Trust- oder
Runtime-Entscheidungen treffen.

Verbindliche Grenzen:

- Patch-Anwendung nur nach Approval,
- keine Path-Traversal-Zugriffe,
- lokale Bridge nur mit Tokenpflicht,
- kein Zugriff auf Black-Tools oder nicht existierende Tool-Pfade,
- keine automatische Weitergabe von Secrets,
- keine lokale Simulation von Marketplace-Freigaben.

## AIR/MCP-Kontext

AIR ist app-neutral; der Module Manager ist Host. MCP- oder Connector-Integration darf AIR-
Grenzen nicht umkehren. Wenn eine Runtime nicht ready ist oder ein Readiness-Gate abläuft,
muss der Connector abbrechen statt Mutationen zu erzwingen.

## Verweise

- [AIR Runtime und Tools entwickeln](10-air-runtime-und-tools.md)
- [Runtime Durability](runtime-durability.md)
- [Pipeline-Architektur KI-Kontext](../ai-handoff/context/pipeline-architecture.md)
- [AIR-Handoff](../air/HANDOFF.md)
