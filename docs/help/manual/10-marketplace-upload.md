# Marketplace-Upload erklärt

## Kurz erklärt

Wenn dein Modul fertig ist, signiert und geprüft wurde, kannst du es auf den **AAIA-Marketplace** hochladen. Der Upload ist nicht dasselbe wie Veröffentlichung — das Paket wird zuerst von AAIA geprüft.

## Wann ist mein Paket upload-bereit?

Dein Paket muss alle diese Bedingungen erfüllen:

- ✅ Trust-Level: mindestens `EtwLocalVerified`
- ✅ Paketprüfung: keine Blocker
- ✅ Marketplace-Token: konfiguriert
- ✅ MoR-Verbindung: aktiv (für kostenpflichtige Module)

Falls eine dieser Bedingungen nicht erfüllt ist, erklärt AAIA dir genau was fehlt.

## Schritt für Schritt

### 1. Upload starten

Öffne den Tab **„Marketplace"** und klicke auf **„Jetzt hochladen"**.

### 2. Warten auf Prüfung

Nach dem Upload ist dein Modul im Status **Pending Review**. Der AAIA-Marketplace prüft das Paket jetzt serverseitig. Das dauert in der Regel einige Stunden bis Tage.

### 3. Ergebnis abwarten

| Status | Bedeutung |
|--------|-----------|
| **Pending Review** | Wird gerade geprüft |
| **MarketplaceVerified** | Prüfung bestanden — wird bald veröffentlicht |
| **MarketplacePublished** | Öffentlich sichtbar und kaufbar |
| **Blocked** | Wurde abgelehnt — du erhältst einen Grund |

## Warum bedeutet Upload nicht sofort Veröffentlichung?

AAIA prüft jedes hochgeladene Modul auf:
- Sicherheit (kein Schadcode, keine verbotenen Dateien)
- Einhaltung der Marketplace-Richtlinien
- Gültige Signatur und Entwickleridentität

Dieses Prüfverfahren schützt alle Nutzer des Marketplaces.

## Wichtig: MarketplaceVerified wird nie lokal gesetzt

Das `MarketplaceVerified`-Feld kann **niemals** durch den Module Manager oder den Entwickler selbst gesetzt werden. Es wird ausschließlich durch den Marketplace-Server gesetzt — nach erfolgreicher Prüfung.

## Häufige Fehler beim Upload

- Keine Internetverbindung → prüfe deine Verbindung
- Falsches Marketplace-Token → in den Einstellungen erneuern
- Trust-Level zu niedrig → erst Signatur erstellen und prüfen
- MoR fehlt → Merchant-of-Record-Verbindung einrichten

## Verwandte Themen

- Trust-Level verstehen
- Lizenzmodelle verstehen
- Marketplace-Upload fehlgeschlagen (Fehlerartikel)
