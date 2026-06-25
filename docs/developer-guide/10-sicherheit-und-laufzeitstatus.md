# Sicherheit und Laufzeitstatus für Entwickler

> Zielgruppe: ETWs, AIR-Entwickler und Host-Entwickler  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerhandbuch-Ergänzung zu Security, Runtime-State und Supportfähigkeit

Sicherheit ist im Entwicklerpfad kein Abschlusscheck, sondern Teil jeder Änderung. Ein Modul,
Plugin oder AIR-Tool muss so gebaut sein, dass Fehler sichtbar werden und unsichere Zustände
kontrolliert stoppen.

## Was Entwickler schützen müssen

- private ETW-Schlüssel,
- Marketplace-Tokens,
- API-Keys,
- lokale Konfigurationsdateien,
- Secret-Store-Inhalte,
- Nutzer- und Serverdaten,
- Runtime-State mit geschütztem Kontext.

## Laufzeitstatus verstehen

AIR-Persistenz und Runtime-State sind opt-in und folgen Phase-10-Regeln:

- alte Phase-9-Checkpoints bleiben lesbar,
- neue Runtime-Änderungen werden als typisierte Deltas geschrieben,
- Snapshots werden verifiziert,
- Operation-IDs sichern externe Seiteneffekte,
- Protectoren schützen lokalen State plattformabhängig,
- fehlende Schlüssel oder falscher Kontext führen fail-closed,
- Readiness-Gates blockieren Mutationen vor abgeschlossener Recovery.

## Entwicklerpflichten bei Runtime-Änderungen

Wenn Code Runtime-State verändert:

1. Mutation als fachlichen Vorgang beschreiben.
2. Operation-ID festlegen.
3. Delta- oder Snapshot-Auswirkung prüfen.
4. Recovery- und Retry-Verhalten testen.
5. Diagnostik ohne Secrets bereitstellen.
6. Backpressure und Shutdown berücksichtigen.

## Supportfähige Fehler

Fehler sollten mit Reason-Code, Komponente und redigierter Diagnose auswertbar sein.
Unbrauchbar sind Fehlermeldungen, die nur „failed“ melden oder Secrets enthalten.

## Fail-closed-Regel

Wenn Signatur, Protector, Permission, Readiness oder Recovery nicht sicher bestätigt werden
können, wird abgebrochen. Entwickler dürfen diese Grenze nicht durch Auto-Fix, Connector,
lokale Dateiänderung oder UI-Schalter umgehen.

## Verweise

- [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md)
- [AIR-State-Architektur](../architecture/air-runtime-state.md)
- [Sicherheit und Laufzeitstatus im Benutzerhandbuch](../user-manual/10-sicherheit-und-laufzeitstatus.md)
