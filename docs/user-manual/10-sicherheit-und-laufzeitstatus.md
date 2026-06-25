# Sicherheit und Laufzeitstatus

> Zielgruppe: Anwender und neue Nutzer  
> Geprüfter Stand: 2026-06-25  
> Status: Anwendererklärung zu implementierten Phase-10-Grundlagen

AAIA trennt normale Bedienung, lokale Entwicklungsfunktionen, serverseitige Prüfung und
KI-gestützte Laufzeitfunktionen. Wenn eine Funktion nicht bereit ist oder ein Sicherheitsnachweis
fehlt, soll sie kontrolliert stoppen statt unsicher weiterzulaufen.

## Was Anwender wissen müssen

- Lokale Signaturen beweisen nur lokale Herkunft und lokale Prüfung.
- Marketplace-Freigaben werden serverseitig gesetzt, nicht durch den Module Manager.
- AIR-Funktionen sind opt-in und nicht automatisch aktiv.
- Persistenz- und Recovery-Funktionen schützen Laufzeitstatus, ersetzen aber kein Backup.
- Fehlende Schlüssel, falscher Kontext oder beschädigter Zustand führen zu fail-closed Verhalten.

## Laufzeitstatus in einfachen Worten

Die AIR-Laufzeit speichert wichtige Zustandsänderungen dauerhaft. Neuere Stände verwenden
typisierte Delta-Ereignisse. Ältere Phase-9-Checkpoints können weiterhin gelesen werden, damit
ein Upgrade kontrolliert möglich bleibt.

Für Anwender bedeutet das:

1. Nach einem Neustart soll die Runtime den letzten gültigen Zustand wiederfinden.
2. Externe Tool-Aktionen werden mit einer Operation-ID abgesichert, damit Wiederholungen nicht
   versehentlich doppelt wirken.
3. Wenn die Runtime beim Start, Recovery oder Shutdown unsicher ist, meldet sie einen Fehler
   statt den Zustand still zu verändern.

## Was nicht in Support-Nachrichten gehört

Gib niemals Tokens, Passwörter, private Schlüssel, vollständige Konfigurationen, echte
Servernamen oder private Benutzerpfade weiter. Für Support reichen Produktversion,
redigierter Fehlercode, Zeitpunkt und eine kurze Beschreibung der Aktion.

## Weitere Hilfe

- [AAIA-Fehlerbehebung](../troubleshooting/index.md)
- [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md)
- [Glossar](../glossary/index.md)
