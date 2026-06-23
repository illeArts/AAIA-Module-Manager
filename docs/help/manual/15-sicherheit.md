# Sicherheit — was du schützen musst

## Kurz erklärt

Als ETW-Entwickler bist du für die Sicherheit deiner Schlüssel und Zugangsdaten selbst verantwortlich. AAIA hilft dir dabei — aber bestimmte Regeln musst du selbst einhalten.

## Was ist ein Private Key?

Dein **Private Key** ist der geheime Teil deines ETW-Entwicklerschlüssels. Er wird verwendet, um Pakete digital zu signieren. Mit diesem Schlüssel „unterschreibst" du, dass ein Paket wirklich von dir stammt.

> ⚠️ **Wenn dein Private Key in falsche Hände gerät, kann jemand anderes Pakete in deinem Namen signieren.**

## Goldene Regel: Was darf nie geteilt werden?

| Was | Warum |
|-----|-------|
| Private Key (`.pem`, `.pfx`, `.p12`) | Kann zur Fälschung deiner Identität missbraucht werden |
| API-Keys | Könnten für unautorisierte Anfragen verwendet werden |
| Marketplace-Token | Erlaubt Uploads in deinem Namen |
| Passwörter | Offensichtlich |
| `.env`-Dateien | Enthalten typischerweise alle oben genannten Geheimnisse |

## Was darf niemals in einen KI-Prompt?

AAIA hat einen eingebauten Sicherheitsfilter. Trotzdem gilt:

- **Niemals** Private Keys kopieren und an eine KI senden
- **Niemals** Marketplace-Tokens in Prompts einfügen
- **Niemals** `.env`-Dateien als Kontext an eine KI geben
- **Nur** Fehlermeldungen, Pipeline-Status und Hilfeartikel teilen

AAIA erzeugt automatisch sichere KI-Kontexte, die diese Regeln einhalten.

## Was bedeutet „AAIA schreibt nie direkt ins Projekt"?

Wenn eine angeschlossene KI (z.B. Claude via Connector) Änderungen vorschlägt, zeigt AAIA immer zuerst einen **Diff** (Vergleich alt ↔ neu) an. Du musst jede Änderung manuell bestätigen.

Eine KI kann niemals ohne deine Zustimmung Dateien in deinem Projekt verändern.

## Wie schütze ich meinen Private Key?

1. Speichere ihn an einem sicheren Ort — nicht im Projektordner
2. Lege niemals `.pem`- oder `.pfx`-Dateien ins Git-Repository
3. Erstelle eine Sicherheitskopie auf einem verschlüsselten USB-Stick
4. Teile ihn mit niemandem — auch nicht mit AAIA-Mitarbeitern

## Was wenn mein Key gestohlen wurde?

Melde dich sofort beim AAIA-Marketplace und lass alle deine veröffentlichten Module temporär sperren. Erstelle dann einen neuen Schlüssel.

## Technische Details

ETW-Schlüssel sind RSA-2048-Schlüsselpaare. Der Private Key ist lokal gespeichert und verlässt niemals den Module Manager. AAIA überträgt ausschließlich den **Public Key** und die **Signatur** an den Marketplace.

## Verwandte Themen

- Signatur & Vertrauen verstehen
- Trust-Level — was bedeuten die Stufen?
- ETW-Signatur konnte nicht geprüft werden (Fehlerartikel)
