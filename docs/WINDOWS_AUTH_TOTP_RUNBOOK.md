# Windows Login/Registration/TOTP Runbook

## Kurzbefund

Der Windows-Fehler liegt sehr wahrscheinlich nicht an Avalonia selbst, sondern an einem Unterschied zwischen Mac-Zustand und Windows-Zustand:

- Auf dem Mac ist vermutlich bereits ein gueltiger Marketplace/ETW-Token in `~/Library/Application Support/AAIAModuleManager/config.json` gespeichert oder die App zeigt auf eine API, die Login erlaubt.
- Auf Windows startet die App haeufig frisch ohne Token. Dadurch wird Registrierung/Login wirklich gegen die konfigurierte Marketplace API ausgefuehrt.
- Der Module-Manager-Client erwartet beim Registrieren eine Antwort mit `TotpUri` oder `TotpSecret`.
- Der aktuell im Repo vorhandene .NET-Backend-Code `aaia-marketplace-api` erzeugt aber kein TOTP-Secret und hat auch keinen `verify-totp`-Endpoint.

Das Ergebnis: Windows legt eventuell einen Account an, bekommt aber keinen Authenticator-Key. Danach kommt beim TOTP-Schritt oder Login ein API-Fehler.

## Was der Client erwartet

Der Module Manager ruft diese Client-Methoden auf:

- Registrierung: `POST developers/register`
- Login: `POST developers/login`
- TOTP bestaetigen: `POST developers/verify-totp`

Bei einer erfolgreichen Registrierung erwartet der Client:

```json
{
  "etwId": "ETW-000042",
  "displayName": "Name",
  "role": "Community",
  "message": "Account angelegt.",
  "totpUri": "otpauth://totp/AAIA:mail@example.com?...",
  "totpSecret": "BASE32SECRET"
}
```

Ohne `totpUri` oder `totpSecret` kann kein QR-Code angezeigt werden. Dann gibt es auch keinen Code fuer Google Authenticator, Microsoft Authenticator oder AAIA Authenticator.

## Was im aktuellen Backend fehlt

Im Repo ist in `../aaia-marketplace-api/src/AAIA.MarketplaceApi/Services/DeveloperService.cs` aktuell sichtbar:

- `RegisterAsync` speichert Account mit Status `Pending`.
- `RegisterAsync` gibt nur ETW-ID, DisplayName, Role und Message zurueck.
- Es wird kein TOTP-Secret erzeugt.
- Es wird kein TOTP-Secret in der Datenbank gespeichert.
- `LoginAsync` prueft kein TOTP.

Im Controller `../aaia-marketplace-api/src/AAIA.MarketplaceApi/Controllers/DevelopersController.cs` gibt es:

- `POST /api/developers/register`
- `POST /api/developers/login`

Es fehlt:

- `POST /api/developers/verify-totp`

## Was in diesem Commit am Client verbessert wurde

Der Client springt nach Registrierung nicht mehr blind auf den TOTP-Screen, wenn die API keinen `totpUri` und keinen `totpSecret` liefert. Stattdessen zeigt er eine klare Fehlermeldung:

> Account wurde angelegt, aber die API hat keinen TOTP-Key geliefert. Bitte Marketplace-API/TOTP-Backend aktualisieren oder die richtige API-URL einstellen.

Zusätzlich wurden vorher schon die Setup-/GitHub-Buttons stabilisiert:

- Prozessausgabe wird auf den Avalonia-UI-Thread marshalled.
- Fehlende Tools wie `gh`, `brew` oder `winget` crashen die App nicht mehr.
- Einstellungen zeigen AAIA-Konto, GitHub-Status und AAIAS-URL.

## Was auf dem Windows-PC geprueft werden muss

### 1. Installierte Version ersetzen

Alten Windows-Installer nicht weiterverwenden, wenn er vor diesem Commit gebaut wurde. Neu bauen:

```powershell
cd C:\Pfad\zu\AAIAGitHub\aaia-module-manager\installer
.\build-installer.bat
```

Dann installieren:

```text
installer\dist\AAIA_ModuleManager_v2.0.0_Setup.exe
```

### 2. Config-Datei auf Windows pruefen

Die App speichert Einstellungen hier:

```text
%AppData%\AAIAModuleManager\config.json
```

Pruefen:

- `MarketplaceApiUrl`
- `MarketplaceToken`
- `DeveloperEtwId`
- `DeveloperDisplayName`

Wenn Windows mit einem falschen/alten Token oder falscher API arbeitet, App schliessen und testweise `MarketplaceToken`, `DeveloperEtwId`, `DeveloperDisplayName` leeren.

### 3. Marketplace API URL vergleichen

Auf Mac und Windows muss exakt dieselbe API-URL eingestellt sein. In der App:

```text
Einstellungen -> Marketplace API URL
```

Typische Varianten:

```text
https://aaiagent.de/index.php?rest_route=/aaia/v1
https://aaiagent.de/wp-json/aaia/v1
http://localhost:5000/api
```

Wichtig: Die Client-URL-Logik haengt relative Routen an diese Basis an. Eine falsche Basis fuehrt zu HTML/404/Cloudflare statt JSON und erscheint dann als API-Fehler.

### 4. API direkt auf Windows testen

PowerShell:

```powershell
$base = "https://aaiagent.de/index.php?rest_route=/aaia/v1"
$body = @{
  displayName = "Windows Test"
  email = "windows-test@example.com"
  password = "TestPassword123!"
  githubAccount = $null
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "$base/developers/register" -ContentType "application/json" -Body $body
```

Die Antwort muss `totpUri` oder `totpSecret` enthalten. Wenn nicht, kann die Windows-App keinen TOTP-Code anzeigen.

### 5. Backend-Fix, falls die .NET Marketplace API genutzt wird

Wenn Windows gegen das .NET-Projekt `aaia-marketplace-api` laeuft, muss dort TOTP erst implementiert werden:

- Datenbankfeld fuer TOTP-Secret hinzufuegen, z.B. `TotpSecret`.
- Bei `RegisterAsync` ein Base32-Secret erzeugen.
- `otpauth://totp/AAIA:<email>?secret=<secret>&issuer=AAIA` als `TotpUri` zurueckgeben.
- `POST /api/developers/verify-totp` implementieren.
- Bei erfolgreicher Verifikation Account von `Pending` auf `Active` setzen und JWT zurueckgeben.
- `LoginAsync` so erweitern, dass bei aktivem TOTP ohne Code eine 401-Meldung wie `TOTP erforderlich` kommt.
- TOTP-Codes mit kleiner Clock-Skew-Toleranz pruefen.

## Warum Mac anders wirkt

Der Mac startet wahrscheinlich nicht denselben frischen Registrierungsfluss:

- gespeicherter Token vorhanden,
- gespeicherte ETW-ID vorhanden,
- andere API-URL,
- oder lokale API/Live-API unterscheidet sich vom Windows-Ziel.

Darum zuerst Config-Dateien vergleichen:

Mac:

```text
~/Library/Application Support/AAIAModuleManager/config.json
```

Windows:

```text
%AppData%\AAIAModuleManager\config.json
```

Die Werte `MarketplaceApiUrl`, `DeveloperEtwId` und `MarketplaceToken` erklaeren meistens den Unterschied.

## Uebergabe an ChatGPT/Codex auf dem PC

Bitte im Windows-Repo diese Schritte ausfuehren:

1. `git pull` ausfuehren und sicherstellen, dass der Commit mit `WINDOWS_AUTH_TOTP_RUNBOOK.md` vorhanden ist.
2. `dotnet build AAIA.ModuleManager.sln` im Ordner `aaia-module-manager` ausfuehren.
3. `installer\build-installer.bat` ausfuehren.
4. Den neuen Installer installieren.
5. In `%AppData%\AAIAModuleManager\config.json` die `MarketplaceApiUrl` mit Mac vergleichen.
6. Registrierung testen.
7. Wenn die App meldet, dass die API keinen TOTP-Key liefert, nicht weiter am Windows-UI suchen: Dann muss das Marketplace-Backend/TOTP implementiert oder die API-URL korrigiert werden.
