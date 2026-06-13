#!/bin/bash
# ============================================================
#  AAIA Module Manager — macOS Build Pipeline
#
#  Erstellt .app-Bundle für Apple Silicon (osx-arm64)
#  und Intel Mac (osx-x64).
#
#  Voraussetzungen:
#    - .NET 8 SDK installiert (https://dotnet.microsoft.com)
#    - aaia-sdk im Geschwister-Ordner: ../aaia-sdk  (für lokale Projektreferenz)
#    - Xcode Command Line Tools: xcode-select --install
#
#  Aufruf:
#    chmod +x installer/build-mac.sh
#    ./installer/build-mac.sh [--arch arm64|x64|both] [--sign "Developer ID Application: ..."]
#
#  Ausgabe:
#    publish/AAIA\ Module\ Manager.app   (arm64 oder letzte Architektur)
#    installer/dist/AAIA_ModuleManager_v2.0.0_arm64.dmg
#    installer/dist/AAIA_ModuleManager_v2.0.0_x64.dmg
# ============================================================

set -e

# ── Konfiguration ─────────────────────────────────────────────
APP_NAME="AAIA Module Manager"
BUNDLE_ID="de.illearts.aaia-module-manager"
VERSION="2.0.0"
EXECUTABLE="AAIA.ModuleManager"
PROJECT="../src/AAIA.ModuleManager/AAIA.ModuleManager.csproj"
PLIST_SRC="$(dirname "$0")/Info.plist"
ICON_SRC="$(dirname "$0")/AppIcon.icns"
DIST_DIR="$(dirname "$0")/dist"

# ── Argumente ─────────────────────────────────────────────────
ARCH="arm64"
SIGN_ID=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch) ARCH="$2"; shift 2 ;;
        --sign) SIGN_ID="$2"; shift 2 ;;
        *) echo "Unbekanntes Argument: $1"; exit 1 ;;
    esac
done

# ── Hilfsfunktion: eine Architektur bauen ─────────────────────
build_arch() {
    local arch="$1"
    local runtime="osx-${arch}"
    local publish_dir="../publish/${runtime}"
    local app_dir="../publish/${APP_NAME}.app"

    echo ""
    echo "══════════════════════════════════════════════"
    echo "  Baue für ${runtime} ..."
    echo "══════════════════════════════════════════════"

    # SDK-Referenz prüfen
    if [ ! -f "../../../aaia-sdk/src/AAIA.Shared.Contracts/AAIA.Shared.Contracts.csproj" ]; then
        echo "⚠  aaia-sdk nicht gefunden unter ../../../aaia-sdk"
        echo "   Bitte sicherstellen, dass beide Repos auf gleicher Ebene liegen:"
        echo "   AAIAGitHub/"
        echo "   ├── aaia-module-manager/"
        echo "   └── aaia-sdk/"
        exit 1
    fi

    # [1/3] dotnet restore
    echo ""
    echo "[1/3]  dotnet restore ..."
    dotnet restore "$PROJECT"

    # [2/3] dotnet publish
    echo ""
    echo "[2/3]  dotnet publish (Release / ${runtime} / self-contained) ..."
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$runtime" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$publish_dir"

    # [3/3] .app-Bundle zusammenbauen
    echo ""
    echo "[3/3]  .app-Bundle erstellen ..."

    rm -rf "$app_dir"
    mkdir -p "${app_dir}/Contents/MacOS"
    mkdir -p "${app_dir}/Contents/Resources"

    # Binary kopieren und ausführbar machen
    cp "${publish_dir}/${EXECUTABLE}" "${app_dir}/Contents/MacOS/${EXECUTABLE}"
    chmod +x "${app_dir}/Contents/MacOS/${EXECUTABLE}"

    # Info.plist
    cp "$PLIST_SRC" "${app_dir}/Contents/Info.plist"

    # Icon (optional)
    if [ -f "$ICON_SRC" ]; then
        cp "$ICON_SRC" "${app_dir}/Contents/Resources/AppIcon.icns"
        echo "   Icon eingebettet: AppIcon.icns"
    else
        echo "   ℹ  Kein AppIcon.icns gefunden — Bundle hat kein Icon."
        echo "      Erstelle mit: xcrun actool oder iconutil (siehe README)."
    fi

    # Codesigning (optional)
    if [ -n "$SIGN_ID" ]; then
        echo ""
        echo "   Codesigning mit: ${SIGN_ID} ..."
        codesign --deep --force --verify --verbose \
            --options runtime \
            --sign "$SIGN_ID" \
            "$app_dir"
        echo "   ✓ Codesigning abgeschlossen."
    else
        echo ""
        echo "   ℹ  Kein --sign übergeben — App ist nicht signiert (nur lokal nutzbar)."
        echo "      Für Distribution: --sign \"Developer ID Application: André Iljaschow (TEAMID)\""
    fi

    echo ""
    echo "  ✓ Bundle fertig: ${app_dir}"
}

# ── Hauptlogik ────────────────────────────────────────────────
mkdir -p "$DIST_DIR"

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   AAIA Module Manager  —  macOS Build        ║"
echo "╚══════════════════════════════════════════════╝"

if [ "$ARCH" = "both" ]; then
    build_arch "arm64"
    build_arch "x64"
elif [ "$ARCH" = "arm64" ] || [ "$ARCH" = "x64" ]; then
    build_arch "$ARCH"
else
    echo "Ungültige Architektur: $ARCH (erlaubt: arm64, x64, both)"
    exit 1
fi

echo ""
echo "══════════════════════════════════════════════"
echo "  Build abgeschlossen."
echo "  App: publish/AAIA Module Manager.app"
echo "  DMG erstellen: ./installer/create-dmg.sh"
echo "══════════════════════════════════════════════"
echo ""
