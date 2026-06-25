# AAIA Module Manager  v2

Dev-Tool für AAIA-Modul- und Plugin-Entwickler — alles auf wenige Klicks reduziert.

**V2 neu:** Live-Verbindung mit AAIAS · FileSystemWatcher · IDE-Erkennung · WorkOrder-Simulator · SSE-Log-Stream

**Tech:** Avalonia 11 · .NET 8 · GitHub CLI · Windows, macOS & Linux

---

## Tabs

| Tab | Funktion |
|-----|----------|
| **SDK / Contracts** | Aktuelle + neueste NuGet-Version · Patch/Minor/Major bumpen · Vollautomatischer Release mit NuGet-Polling |
| **Mein Modul** | Modul/Plugin aus Template scaffolden · Build · Bump & Push · Registry-Eintrag |
| **Registry** | Alle registrierten AAIA-Extensions · Suchen · Eintrag hinzufügen |
| **Tester** | Mit AAIAS verbinden · Projekt beobachten (FileWatcher) · Build → Install → Enable · Hot-Reload · WorkOrder simulieren · SSE-Live-Log |
| **Setup** | git / gh / .NET prüfen · GitHub Login · Pfade + AAIAS-Credentials konfigurieren |
| **Developer** | Marketplace-Login · Publisher-Schlüssel verwalten |
| **Publish** | Build, Signieren & Marketplace-Upload in einem Schritt |

---

## Installation

Die neueste Version ist auf der [offiziellen AAIA-Website](https://aaiagent.de) oder unter [Releases](https://github.com/illeArts/AAIA-Module-Manager/releases/latest) verfügbar.

**Windows**
1. `AAIA.ModuleManager.exe` herunterladen
2. Beliebig ablegen — kein Installer nötig
3. Beim ersten Start: **Setup**-Tab → Pfade, GitHub Login und AAIAS-URL konfigurieren

**macOS**
1. `AAIA.ModuleManager` (macOS-Binary) herunterladen
2. Beim ersten Start evtl. Gatekeeper-Warnung: `Rechtsklick → Öffnen` oder in Systemeinstellungen freigeben
3. Beim ersten Start: **Setup**-Tab → Pfade, GitHub Login und AAIAS-URL konfigurieren

**Linux**
1. Das passende `.deb`-Paket oder das Linux-Binary herunterladen
2. Debian/Ubuntu: `sudo apt install ./AAIA_ModuleManager_*_linux-x64.deb`
3. Alternativ Binary ausführbar machen: `chmod +x AAIA.ModuleManager`
4. Beim ersten Start: **Setup**-Tab → Pfade, GitHub Login und AAIAS-URL konfigurieren

---

## Voraussetzungen

| | Windows | macOS | Linux |
|---|---|---|---|
| OS | Windows 10/11 x64 | macOS 12+ (Apple Silicon & Intel) | Debian/Ubuntu-kompatibel x64/arm64 |
| git | [git-scm.com](https://git-scm.com/) | Xcode Command Line Tools (`xcode-select --install`) | Paketmanager, z. B. `sudo apt install git` |
| GitHub CLI | via Setup-Tab (winget) | via Setup-Tab (Homebrew) | Paketmanager oder [cli.github.com](https://cli.github.com/) |
| .NET 8 SDK | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| AAIAS | `localhost:5174` (für Tester-Tab) | `localhost:5174` (für Tester-Tab) | `localhost:5174` (für Tester-Tab) |

---

## Tester-Tab — Workflow

```
1. AAIAS starten (localhost:5174)
2. Setup-Tab: AAIAS-URL + Credentials speichern
3. Tester-Tab → "Verbinden"
4. Projektordner wählen (Ordner mit .csproj + aaia-extension.json)
5. "Build & Publish"
6. "Install + Enable"
7. Optional: "▶ Stream" für Live-SSE-Diagnose
8. Optional: WorkOrder JSON eingeben → "Simulieren" (erfordert Dev Mode)
```

---

## Selbst bauen

**Windows**
```powershell
git clone https://github.com/illeArts/AAIA-Module-Manager.git
cd AAIA-Module-Manager
dotnet publish src/AAIA.ModuleManager/AAIA.ModuleManager.csproj `
    -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true -o ./publish
```

**macOS**
```bash
git clone https://github.com/illeArts/AAIA-Module-Manager.git
cd AAIA-Module-Manager
dotnet publish src/AAIA.ModuleManager/AAIA.ModuleManager.csproj \
    -c Release -r osx-arm64 --self-contained true \
    /p:PublishSingleFile=true -o ./publish
# Für Intel Mac: -r osx-x64
```

**Linux**
```bash
git clone https://github.com/illeArts/AAIA-Module-Manager.git
cd AAIA-Module-Manager
dotnet publish src/AAIA.ModuleManager/AAIA.ModuleManager.csproj \
    -c Release -r linux-x64 --self-contained true \
    /p:PublishSingleFile=true -o ./publish
# Für ARM64: -r linux-arm64
```

Mit Paketbau:

```bash
chmod +x installer/build-linux.sh
./installer/build-linux.sh --arch x64
# Für ARM64 oder beide Architekturen:
# ./installer/build-linux.sh --arch arm64
# ./installer/build-linux.sh --arch both
```

Ausgaben:

```text
publish/linux-x64/AAIA.ModuleManager
installer/dist/AAIA_ModuleManager_*_linux-x64.deb
```

### macOS/Xcode-Build

Das Projekt enthält zusätzlich `AAIA.ModuleManager.xcodeproj` als Xcode-External-Build-Target. Xcode startet dabei die bestehende .NET/Avalonia-Build-Pipeline:

```bash
open AAIA.ModuleManager.xcodeproj
```

In Xcode das Scheme **AAIA Module Manager** bauen. Alternativ direkt per Terminal:

```bash
chmod +x installer/*.sh
./installer/create-icns.sh src/AAIA.ModuleManager/Assets/AAIA_Module_Manager.png
./installer/build-mac.sh --arch arm64
./installer/create-dmg.sh --arch arm64
```

Ausgaben:

```text
publish/AAIA Module Manager-arm64.app
installer/dist/AAIA_ModuleManager_v2.0.0_arm64.dmg
```

Lokale Builds werden ad hoc signiert. Für öffentliche macOS-Verteilung wird ein Apple-Developer-Zertifikat plus Notarisierung benötigt:

```bash
./installer/build-mac.sh --arch arm64 --sign "Developer ID Application: Name (TEAMID)"
```

---

## Contracts-Release

```
SDK / Contracts Tab → "Bump Minor" klicken → fertig
```

Intern läuft `release-contracts.ps1` — bumpt Version, committed, tagged, wartet auf NuGet, updated alle .csproj im Monorepo.

---

**© André Iljaschow / IleArts**
