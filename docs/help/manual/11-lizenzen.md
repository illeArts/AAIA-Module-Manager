# Lizenzmodelle verstehen

## Kurz erklärt

Als ETW-Entwickler entscheidest du, wie Nutzer auf dein Modul zugreifen können. AAIA unterstützt fünf Lizenztypen.

## Die fünf Lizenztypen

### 🆓 Free

Das Modul ist kostenlos. Jeder Nutzer kann es installieren und nutzen ohne zu zahlen.

**Geeignet für:** Einsteigermodule, Community-Projekte, Open-Source-Erweiterungen.

---

### 🕐 Trial

Das Modul hat eine kostenlose Testphase — danach muss der Nutzer kaufen oder das Modul wird deaktiviert.

**Geeignet für:** Module bei denen Nutzer erst überzeugt werden müssen.

---

### 💰 Paid

Einmalige Zahlung — der Nutzer kauft das Modul einmal und hat dauerhaften Zugriff.

**Geeignet für:** Spezialtools, einmalige Werkzeuge ohne laufende Updates.

---

### 🔄 Subscription

Monatliche oder jährliche Zahlung. Der Nutzer zahlt regelmäßig und hat solange Zugriff, wie das Abonnement aktiv ist.

**Geeignet für:** Module mit laufenden Updates, KI-Dienste mit API-Kosten.

---

### 🏢 Enterprise

Individuelle Lizenzvereinbarung für Unternehmenskunden. Preise und Bedingungen werden direkt verhandelt.

**Geeignet für:** Großkundenlösungen, On-Premise-Deployments.

## Wo wird der Lizenztyp festgelegt?

Im Manifest (`aaia-extension.json`) im Feld `licenseType`. Du kannst ihn im Module Manager unter **„Projekteinstellungen"** ändern.

## Was passiert wenn eine Lizenz abläuft?

- **Trial:** Das Modul wird nach der Testphase deaktiviert und fordert zum Kauf auf.
- **Subscription:** Das Modul wird nach Ende des Abonnements deaktiviert.
- **Paid:** Läuft nicht ab — einmal bezahlt, dauerhaft verfügbar.

## Verwalten Käufer ihre Lizenzen selbst?

Nein. Lizenzen werden zentral auf dem AAIA-Marketplace verwaltet und mit dem AAIAS-Konto des Käufers verknüpft. Als Entwickler siehst du im **Entwickler-Dashboard** eine Übersicht deiner Verkäufe.

## Technische Details

AAIA verwendet Lemon Squeezy oder Paddle als Zahlungsanbieter (Merchant of Record). Du verarbeitest keine Zahlungen selbst — das übernimmt der MoR. Du erhältst 70 % des Verkaufspreises.

## Verwandte Themen

- Marketplace-Upload erklärt
- Lizenz erforderlich (Fehlerartikel)
- Nutzer, Käufer, ETW, Admin — wer ist was?
