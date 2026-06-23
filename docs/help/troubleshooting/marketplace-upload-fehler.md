# Marketplace-Upload fehlgeschlagen / Publish Gate blockiert

## Problem: Upload fehlgeschlagen

### Was bedeutet das?

Der Versuch, dein Paket auf den AAIA-Marketplace hochzuladen, ist fehlgeschlagen. Das Paket wurde **nicht** übertragen.

### Ursache 1: Keine Internetverbindung

Prüfe ob du eine aktive Internetverbindung hast. Versuche eine Website zu öffnen. Wenn das nicht funktioniert, behebe zuerst die Netzwerkverbindung.

### Ursache 2: Marketplace-Token fehlt oder ist abgelaufen

Dein Marketplace-Token ist der Zugangscode für deinen ETW-Account beim Marketplace.

**Lösung:**
1. Öffne AAIA → **Einstellungen** → **Marketplace-Konto**
2. Klicke auf **„Token erneuern"**
3. Melde dich beim Marketplace an und kopiere den neuen Token
4. Füge ihn in AAIA ein

> ⚠️ **Sicherheitshinweis:** Teile deinen Marketplace-Token niemals mit anderen Personen oder KI-Systemen.

### Ursache 3: Trust-Level zu niedrig

Das Paket muss mindestens `EtwLocalVerified` haben, bevor ein Upload möglich ist.

**Was zu tun ist:**
1. Öffne den Tab **„Signatur"**
2. Erstelle die ETW-Signatur (falls noch nicht geschehen)
3. Prüfe die Signatur
4. Versuche den Upload erneut

### Ursache 4: Paketprüfung nicht bestanden

AAIA prüft das Paket vor dem Upload auf Sicherheitsprobleme. Wenn die Prüfung Blocker gefunden hat, wird der Upload blockiert.

**Was zu tun ist:**
1. Öffne den Tab **„Paketprüfung"**
2. Behebe alle Blocker
3. Starte die Paketprüfung erneut
4. Versuche dann den Upload

---

## Problem: Publish Gate blockiert

### Was bedeutet das?

AAIA hat erkannt, dass eine oder mehrere Voraussetzungen für die Veröffentlichung nicht erfüllt sind, und hat den Upload-Prozess gestoppt.

Das ist kein Fehler — es ist ein Schutz. AAIA stellt sicher, dass nur vollständige, signierte und geprüfte Pakete hochgeladen werden.

### Was fehlt?

AAIA zeigt dir genau welche Bedingungen nicht erfüllt sind:

| Bedingung | Was zu tun |
|-----------|------------|
| Trust-Level zu niedrig | Signatur erstellen + prüfen |
| Paketprüfung ausstehend | Paketprüfung starten + Blocker beheben |
| MoR-Verbindung fehlt | Merchant of Record einrichten (für kostenpflichtige Module) |
| Marketplace-Token fehlt | Token in Einstellungen erneuern |

### MoR-Verbindung — was ist das?

Für kostenpflichtige Module (Paid, Subscription, Trial, Enterprise) benötigst du eine **Merchant-of-Record-Verbindung**. Das ist eine Verbindung zu einem Zahlungsanbieter (Lemon Squeezy oder Paddle), über den Käufer zahlen.

Ohne MoR-Verbindung kannst du kein kostenpflichtiges Modul veröffentlichen — weil sonst niemand bezahlen könnte.

**Lösung:** Öffne den **„Developer"-Tab** → **„Zahlungsanbieter einrichten"**

## Verwandte Themen

- Marketplace-Upload erklärt
- Trust-Level verstehen
- Signatur & Vertrauen verstehen
