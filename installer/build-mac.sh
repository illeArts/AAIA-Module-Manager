#!/bin/bash
# ============================================================
#  AAIA Module Manager — macOS Build Pipeline
#
#  Erstellt .app-Bundle für Apple Silicon (osx-arm64)
#  und Intel Mac (osx-x64).
#
#  Voraussetzungen:
#    - .NET 8 SDK installiert (https://dotnet.microsoft.com)
#    - aaia-sdk im Geschwister-Ordner: ../aaia-sdk
#    - Xcode Command Line Tools: xcode-select --install
#
#  Aufruf:
#    chmod +x installer/build-mac.sh
#    ./installer/build-mac.sh [--arch arm64|x64|both] [--sign "Developer ID Application: ..."]
# ============================================================

set -e

# Xcode GUI builds starten mit einem deutlich kleineren PATH als Terminal-Shells.
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:${PATH:-}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
LOG_PATH="${REPO_ROOT}/xcode-build.log"
exec > >(tee "$LOG_PATH") 2>&1

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${REPO_ROOT}/.dotnet-home}"
export HOME="${HOME:-${DOTNET_CLI_HOME}}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${DOTNET_CLI_HOME}/.nuget/packages}"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"

APP_NAME="AAIA Module Manager"
VERSION="2.0.0"
EXECUTABLE="AAIA.ModuleManager"
PROJECT="${REPO_ROOT}/src/AAIA.ModuleManager/AAIA.ModuleManager.csproj"
SDK_PROJECT="${REPO_ROOT}/../aaia-sdk/src/AAIA.Shared.Contracts/AAIA.Shared.Contracts.csproj"
PLIST_SRC="${SCRIPT_DIR}/Info.plist"
ICON_SRC="${SCRIPT_DIR}/AppIcon.icns"
DIST_DIR="${SCRIPT_DIR}/dist"
PUBLISH_ROOT="${REPO_ROOT}/publish"
DOTNET_CMD="${DOTNET_CMD:-}"

if [ -z "$DOTNET_CMD" ]; then
    for candidate in \
        "/opt/homebrew/bin/dotnet" \
        "/usr/local/bin/dotnet" \
        "/usr/local/share/dotnet/dotnet" \
        "$(command -v dotnet 2>/dev/null || true)"
    do
        if [ -n "$candidate" ] && [ -x "$candidate" ]; then
            DOTNET_CMD="$candidate"
            break
        fi
    done
fi

run_dotnet() {
    env -i \
        PATH="$PATH" \
        HOME="$HOME" \
        DOTNET_CLI_HOME="$DOTNET_CLI_HOME" \
        DOTNET_ROOT="${DOTNET_ROOT:-}" \
        NUGET_PACKAGES="$NUGET_PACKAGES" \
        TMPDIR="${TMPDIR:-/tmp}" \
        LANG="${LANG:-en_US.UTF-8}" \
        LC_ALL="${LC_ALL:-en_US.UTF-8}" \
        "$DOTNET_CMD" "$@"
}

ARCH="arm64"
SIGN_ID=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch) ARCH="$2"; shift 2 ;;
        --sign) SIGN_ID="$2"; shift 2 ;;
        *) echo "Unbekanntes Argument: $1"; exit 1 ;;
    esac
done

build_arch() {
    local arch="$1"
    local runtime="osx-${arch}"
    local publish_dir="${PUBLISH_ROOT}/${runtime}"
    local app_dir="${PUBLISH_ROOT}/${APP_NAME}-${arch}.app"

    echo ""
    echo "══════════════════════════════════════════════"
    echo "  Baue für ${runtime} ..."
    echo "══════════════════════════════════════════════"

    if [ ! -f "$SDK_PROJECT" ]; then
        echo "⚠  aaia-sdk nicht gefunden unter ${SDK_PROJECT}"
        echo "   Bitte sicherstellen, dass beide Repos auf gleicher Ebene liegen:"
        echo "   AAIAGitHub/"
        echo "   ├── aaia-module-manager/"
        echo "   └── aaia-sdk/"
        exit 1
    fi

    if [ -z "$DOTNET_CMD" ]; then
        echo "✗ dotnet wurde nicht gefunden."
        echo "  Installiere .NET 8 SDK oder setze DOTNET_CMD auf den absoluten dotnet-Pfad."
        echo "  Erwartete Pfade: /opt/homebrew/bin/dotnet oder /usr/local/share/dotnet/dotnet"
        exit 1
    fi

    echo ""
    echo "[1/3]  dotnet restore ..."
    echo "       ${DOTNET_CMD}"
    run_dotnet restore "$PROJECT"

    echo ""
    echo "[2/3]  dotnet publish (Release / ${runtime} / self-contained) ..."
    run_dotnet publish "$PROJECT" \
        -c Release \
        -r "$runtime" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$publish_dir"

    echo ""
    echo "[3/3]  .app-Bundle erstellen ..."

    rm -rf "$app_dir"
    mkdir -p "${app_dir}/Contents/MacOS"
    mkdir -p "${app_dir}/Contents/Resources"

    cp "${publish_dir}/${EXECUTABLE}" "${app_dir}/Contents/MacOS/${EXECUTABLE}"
    chmod +x "${app_dir}/Contents/MacOS/${EXECUTABLE}"

    # Single-file bündelt Managed-Code, aber Avalonia/Skia braucht native .dylib-Dateien daneben.
    find "$publish_dir" -maxdepth 1 -name "*.dylib" -exec cp {} "${app_dir}/Contents/MacOS/" \;

    cp "$PLIST_SRC" "${app_dir}/Contents/Info.plist"

    if [ -f "$ICON_SRC" ]; then
        cp "$ICON_SRC" "${app_dir}/Contents/Resources/AppIcon.icns"
        echo "   Icon eingebettet: AppIcon.icns"
    else
        echo "   ℹ  Kein AppIcon.icns gefunden — Bundle hat kein Icon."
        echo "      Erstelle mit: installer/create-icns.sh"
    fi

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
        echo "   Ad-hoc-Codesigning für lokalen macOS-Start ..."
        codesign --deep --force --verify --verbose \
            --sign - \
            "$app_dir"
        echo "   ✓ Ad-hoc-Codesigning abgeschlossen."
        echo "   ℹ  Kein Developer-ID-Zertifikat übergeben — nur lokal/unsigniert distributierbar."
        echo "      Für Distribution: --sign \"Developer ID Application: André Iljaschow (TEAMID)\""
    fi

    echo ""
    echo "  ✓ Bundle fertig: ${app_dir}"

    rm -f "${PUBLISH_ROOT}/${APP_NAME}.app"
    ln -s "$(basename "$app_dir")" "${PUBLISH_ROOT}/${APP_NAME}.app"
}

mkdir -p "$DIST_DIR"

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   AAIA Module Manager  —  macOS Build        ║"
echo "╚══════════════════════════════════════════════╝"
echo "  Script: xcode-env-clean-v2"
echo "  Log: ${LOG_PATH}"
echo "  PATH: ${PATH}"

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
echo "  App: ${PUBLISH_ROOT}/AAIA Module Manager.app"
echo "  DMG erstellen: ${SCRIPT_DIR}/create-dmg.sh --arch ${ARCH}"
echo "══════════════════════════════════════════════"
echo ""
