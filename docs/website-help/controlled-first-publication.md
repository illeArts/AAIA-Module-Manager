# Controlled First Publication

> Geprüfter Stand: 2026-06-25  
> Status: vorbereitet, ohne approved Gate blockiert  
> Scope: Website-Staging, PDF-Finalisierung, In-App-Hilfepaket und AAIAM-Import-Dry-Run

Phase 11.5.12 beschreibt die kontrollierte Erstveröffentlichung gegen echte Zielbedingungen.
Sie ist kein freier Produktivbetrieb und kein automatisches Deployment.

## Zweck

Die vorbereiteten Release-Candidate-Artefakte sollen kontrolliert gegen reale Zielbedingungen
geprüft werden:

- Website nur in Staging,
- PDF nur lokal finalisieren oder sauber überspringen,
- In-App-Hilfe nur als lokales Paket vorbereiten,
- AAIAM nur als Dry Run gegen verfügbare Bibliothek oder fail-closed.

## Release Candidate, Staging und Live

| Begriff | Bedeutung |
|---|---|
| Release Candidate | lokales, hashgeprüftes Ausgabepaket unter `docs/.release-candidate/` |
| Staging | kontrollierte Zielablage zur Prüfung, keine öffentliche Live-Route |
| Live | öffentliche Veröffentlichung; nicht Bestandteil dieser Phase |

## Gate-Voraussetzungen

Ausführung ist nur zulässig, wenn alle Bedingungen erfüllt sind:

- `gateStatus: approved`,
- `approvedBy` und `approvedAtUtc` sind gesetzt,
- passende Allow-Flag ist `true`,
- Review-Checklist ist abgeschlossen,
- RC-Manifest-Hashes sind gültig,
- Target ist explizit aktiviert und konfiguriert.

Wenn eine Bedingung fehlt, muss der Adapter mit `EXECUTION: BLOCKED` enden.

## Zieladapter

| Target | Modus | Grenze |
|---|---|---|
| Website Staging | `staging` | kein Domainwechsel, kein Live-Upload |
| PDF Finalization | `final` | lokale Finalisierung, keine öffentliche Veröffentlichung |
| In-App Help Package | `package` | Paket, keine App-Aktivierung |
| AAIAM Import Dry-Run | `dry_run` | kein produktiver Import ohne spätere eigene Phase |

## Rollback- und Audit-Pflicht

Jeder Lauf schreibt `docs/.release-candidate/execution-audit.json`. Das Audit muss Quelle,
Quellcommit, Ziel, Modus, Ergebnis, Reason-Code, Artefakt-Hashes, Secret-/Pfadprüfung und
`notLiveDeployment: true` enthalten.

## AAIAM-Regeln

- Markdown bleibt kanonische Quelle.
- AAIAM erhält nur validierte, redigierte und versionierte Inhalte.
- Historische Rohtexte bleiben gesperrt.
- Keine Secrets, privaten Pfade, Schlüssel, Tokens oder offenen Begriffslangformen.
- Ohne Bibliothek oder Zielkonfiguration bleibt der Zustand fail-closed.

## Keine automatische Veröffentlichung durch KI

Eine KI darf keine Freigabe setzen und keine Veröffentlichung auslösen. Sie darf nur Prüfungen
vorbereiten, Audit-Ausgaben erzeugen und Blocker melden.
