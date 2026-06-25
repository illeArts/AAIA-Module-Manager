# Fehleranalyse und Diagnose

> Zielgruppe: ETWs, Integrationsentwickler und Maintainer  
> Geprüfter Stand: 2026-06-25  
> Status: Entwicklerhandbuch-Ergänzung für reproduzierbare und sichere Fehleranalyse

Fehleranalyse im AAIA-Entwicklerpfad muss reproduzierbar, sicher und zielgerichtet sein.
Diagnose darf helfen, Ursachen zu finden, darf aber keine Sicherheitsprüfungen deaktivieren
oder Secrets offenlegen.

## Mindestinformationen

Für eine brauchbare Analyse werden benötigt:

- Produkt- oder Tool-Version,
- betroffener Schritt, z. B. Validierung, Build, Signatur, Upload oder AIR-Runtime,
- exakter Fehlercode oder Reason-Code,
- redigierter Logausschnitt,
- betroffene Erweiterungs-ID und Version, falls relevant,
- letzte bewusste Änderung,
- erwartetes und tatsächliches Verhalten.

Nicht benötigt und nicht zulässig sind private Schlüssel, Tokens, Passwörter, vollständige
Konfigurationen, echte Serverzugänge oder private Benutzerpfade.

## Diagnose nach Bereich

| Bereich | Typische Ursache | Sicherer nächster Schritt |
|---|---|---|
| Manifest | Pflichtfeld fehlt, Host oder Version unplausibel | Manifest gegen [Manifest und Permissions](04-manifest-und-permissions.md) prüfen |
| Permissions | zu breite oder fehlende Berechtigung | Rechte minimalisieren und fachlich begründen |
| Build | SDK, NuGet, Syntax oder Assembly-Referenz | echte Buildmeldung auswerten, Prüfung nicht abschalten |
| Signatur | Paket nach Signatur verändert, falscher Schlüssel, falsche ETW-ID | Signaturpfad neu ausführen, Private Key nicht teilen |
| Marketplace | Trust-Level zu niedrig, Token ungültig, MoR offen | [Marketplace-Upload](07-marketplace-upload.md) prüfen |
| Connector/KI | ungeeigneter Kontext, fehlendes Approval, Sicherheitsfilter | Handoff reduzieren und [KI-Handoff und Connector](08-ki-handoff-und-connector.md) beachten |
| AIR Runtime | Readiness, Backpressure, Protector oder Recovery | [Runtime-State und AIR-Fehler](../troubleshooting/runtime-state-und-air.md) verwenden |

## Vorgehen

1. Fehler reproduzieren oder Zeitpunkt eingrenzen.
2. Blockierende Sicherheitsprüfung identifizieren.
3. Logdaten redigieren.
4. Manifest, Paketinhalt und Trust-Level prüfen.
5. Bei Runtime-Fehlern Reason-Code und Recovery-Status sichern.
6. Änderung klein halten und erneut validieren.

## Was nicht getan werden darf

- keine Prüfungen deaktivieren, um den nächsten Schritt zu erzwingen,
- keine Marketplace-Status lokal setzen,
- keine Operation-IDs verändern, um Wiederholungen auszulösen,
- keine Secrets in KI-Kontexte oder Tickets kopieren,
- keine State-Dateien manuell reparieren, wenn Recovery einen kontrollierten Fehler meldet.

## Gute Fehlermeldungen

Eine gute Fehlermeldung nennt Komponente, Ursache, betroffene Aktion und sicheren nächsten
Schritt. Sie enthält keine Secrets und keinen vollständigen privaten Pfad.

Beispiel:

```text
Signaturprüfung fehlgeschlagen: Paketinhalt wurde nach der Signatur verändert.
Erstelle das Paket neu, signiere erneut und prüfe danach EtwLocalVerified.
```

## Verweise

- [Validierung, Build und Paketierung](03-validierung-build-paketierung.md)
- [Signatur und Trust-Level](06-signatur-und-trust-level.md)
- [Marketplace-Upload](07-marketplace-upload.md)
- [Sicherheit und Laufzeitstatus für Entwickler](10-sicherheit-und-laufzeitstatus.md)
