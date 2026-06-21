# ETW-Signatur-Fehler beheben

## "Kanonischer Payload manipuliert"

**Ursache:** `signature-info.json` wurde nach der Signierung verändert.

**Lösung:** Signatur neu erstellen (Phase 4.1 wiederholen).

---

## "RSA-Signatur ungültig"

**Mögliche Ursachen:**
1. `.aaiaext`-Paket nach der Signierung neu erstellt (Hash ändert sich).
2. `signature-info.json`-Felder manuell editiert.
3. Falscher Schlüssel verwendet (ETW-ID-Mismatch).

**Lösung:** Prüfe ob die ETW-ID in `signature-info.json` (`developerEtwId`) mit dem lokalen Schlüssel (`%APPDATA%\AAIA\Keys\`) übereinstimmt. Wenn nicht: korrekten Schlüssel laden oder neu generieren.

---

## "Paket nach Signatur verändert"

**Ursache:** Das `.aaiaext`-Paket wurde nach der Signierung neu gebaut oder verändert.

**Lösung:** Signatur neu erstellen — erst nach dem finalen Paketbau signieren.

---

## "Keine Signaturdatei gefunden"

Phase 4.0 (Hash-Vorbereitung) wurde noch nicht ausgeführt. Zuerst den Schritt "🔐 Hash" in der Schaltflächenleiste ausführen.

---

## "Öffentlicher Schlüssel konnte nicht geladen werden"

`signature-info.json` enthält ein beschädigtes oder fehlendes `publicKey`-Feld.

**Lösung:** Signatur neu erstellen (Phase 4.1 wiederholen).

---

## "ETW-ID nicht konfiguriert"

Im Setup-Tab unter "AAIA-Konfiguration" eine gültige ETW-Entwickler-ID eintragen. Format: `{nachname}.{vorname}` (Groß-/Kleinschreibung irrelevant).
