# Rollen und Betriebsgrenzen

> Zielgruppe: Betreiber und Administratoren  
> Geprüfter Stand: 2026-06-25  
> Status: Betriebsleitlinie, keine vollständige IAM-Spezifikation

Administratoren betreiben AAIAS, prüfen Betriebszustände und führen Recovery- oder
Wartungsmaßnahmen aus. Sie ersetzen nicht den Marketplace und erhalten keinen Zugriff auf
private ETW-Schlüssel.

## Betriebsrollen

| Rolle | Aufgabe |
|---|---|
| Betreiber | Plattform, Updates, Monitoring und Betriebsbereitschaft |
| Administrator | lokale und serverseitige Verwaltungsaktionen |
| ETW | Erstellung und lokale Signatur von Erweiterungen |
| Marketplace | serverseitige Prüfung, Freigabe, Sperrung und Veröffentlichung |
| Support | Analyse anhand redigierter Logs und Fehlercodes |

## Grenzen

- Keine lokale Admin-Aktion setzt `MarketplaceVerified`.
- Keine Recovery-Aktion umgeht Signatur- oder Permission-Prüfung.
- Keine Support-Anfrage darf private Schlüssel oder Tokens verlangen.
- Keine Produktionsänderung ohne nachvollziehbaren Grund und Audit.

## Mindestinformationen für Betriebsentscheidungen

- Produkt und Version,
- betroffene Komponente,
- Fehler- oder Reason-Code,
- redigierter Logausschnitt,
- letzter sicherer Zustand,
- getroffene Maßnahme und Ergebnis.
