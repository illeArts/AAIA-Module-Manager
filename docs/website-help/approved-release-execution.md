# Approved Release Execution Adapter

> Geprüfter Stand: 2026-06-25  
> Status: Adapter vorbereitet, Ausführung blockiert  
> Scope: Website-, PDF-, In-App- und AAIAM-Ausgaben nach manuellem Gate

Phase 11.5.11 definiert die Ausführungsadapter für Release-Candidate-Artefakte. Die Adapter
dürfen erst arbeiten, wenn das manuelle Gate aus Phase 11.5.10 freigegeben ist. Ohne approved
Gate ist das korrekte Verhalten `blocked`.

## Zweck der Adapter

Die Adapter übersetzen geprüfte Release-Candidate-Artefakte in konkrete, nachgelagerte
Ausführungsschritte. Sie sind keine Freigabeinstanz und dürfen keine Entscheidung ersetzen.

## Gate-Abhängigkeit

Vor jeder Ausführung müssen erfüllt sein:

- `docs/export/release-gate-manifest.json` hat `gateStatus: approved`,
- `approvedBy` und `approvedAtUtc` sind gesetzt,
- die passende Allow-Flag ist `true`,
- die Review-Checklist ist fachlich abgeschlossen,
- das RC-Manifest und alle Hashes sind gültig.

## Erlaubte Ausführungsschritte

| Adapter | Gate-Flag | Erlaubter Schritt nach Freigabe |
|---|---|---|
| WebsiteExecutionAdapter | `deploymentAllowed` | lokales Prüfen/Kopieren in ein konfiguriertes Staging-Ziel |
| PdfPublicationAdapter | `pdfPublicationAllowed` | kontrollierte lokale PDF-Finalisierung |
| InAppHelpPackagingAdapter | `inAppPackagingAllowed` | lokales Paketieren für spätere App-Integration |
| AaiamImportAdapter | `importAllowed` | Importpaket prüfen oder importieren, wenn Bibliothek und Zielkonfiguration verfügbar sind |

## Verbotene automatische Ausführung

- Keine automatische Freigabe.
- Kein Website-Deployment ohne approved Gate.
- Kein Server-Upload ohne separate Zielkonfiguration.
- Keine produktive AAIAM-DB-Befüllung ohne approved Gate, Bibliothek und Zielkonfiguration.
- Keine finale PDF-Veröffentlichung ohne approved Gate.
- Keine In-App-Hilfe-UI-Aktivierung ohne approved Gate.

## Rollback- und Audit-Anforderungen

Jeder Adapter muss einen Audit-Plan ausgeben. Ein späterer Ausführungslauf muss mindestens
Quelle, Quellcommit, Manifest-Hash, Artefakt-Hashes, Ziel, Zeitpunkt, Operator und Ergebnis
protokollieren. Veröffentlichte oder importierte Artefakte müssen auf das RC-Manifest
zurückführbar bleiben.

## AAIAM-Abgrenzung

AAIAM ist in dieser Phase fail-closed. Ohne verfügbare AAIAM-Bibliothek und ohne explizite
Zielkonfiguration darf kein Import stattfinden. Der Adapter darf nur begründen, warum der Import
blockiert ist, zum Beispiel `aaiam_library_unavailable`.

## Maschinenlesbarer Plan

Der Ausführungsplan liegt unter [`../export/release-execution-plan.json`](../export/release-execution-plan.json).
Initial bleibt `executionStatus` auf `blocked`, und alle Targets bleiben deaktiviert.

## Kontrollierter Erstlauf

Phase 11.5.12 erweitert den Adapter um kontrollierte Zielmodi:

- Website bleibt `staging`,
- PDF bleibt lokale Finalisierung,
- In-App-Hilfe bleibt Paket ohne Aktivierung,
- AAIAM bleibt `dry_run` oder fail-closed.

Details stehen in [`controlled-first-publication.md`](controlled-first-publication.md).
