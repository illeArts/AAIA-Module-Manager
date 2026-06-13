#!/bin/bash
# ============================================================
#  AAIA Module Manager — DMG-Installer erstellen
#
#  Voraussetzung: build-mac.sh zuerst ausführen.
#  Optional: brew install create-dmg  (schöneres DMG mit Background)
#
#  Aufruf:
#    ./installer/create-dmg.sh [--arch arm64|x64]
#
#  Ausgabe:
#    installer/dist/AAIA_ModuleManager_v2.0.0_arm64.dmg
# ============================================================

set -e

<<<<<<< HEAD
APP_NAME="AAIA Module Manager"
VERSION="2.0.0"
ARCH="arm64"
APP_PATH="../publish/${APP_NAME}.app"
DIST_DIR="$(dirname "$0")/dist"
=======
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
APP_NAME="AAIA Module Manager"
VERSION="2.0.0"
ARCH="arm64"
DIST_DIR="${SCRIPT_DIR}/dist"
>>>>>>> 137763b (Prepare Module Manager macOS build)

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch) ARCH="$2"; shift 2 ;;
        *) echo "Unbekanntes Argument: $1"; exit 1 ;;
    esac
done

DMG_NAME="AAIA_ModuleManager_v${VERSION}_${ARCH}.dmg"
DMG_PATH="${DIST_DIR}/${DMG_NAME}"
<<<<<<< HEAD
=======
APP_PATH="${REPO_ROOT}/publish/${APP_NAME}-${ARCH}.app"
>>>>>>> 137763b (Prepare Module Manager macOS build)

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   AAIA Module Manager  —  DMG erstellen      ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

if [ ! -d "$APP_PATH" ]; then
    echo "✗ .app nicht gefunden: ${APP_PATH}"
<<<<<<< HEAD
    echo "  Zuerst ausführen: ./installer/build-mac.sh --arch ${ARCH}"
=======
    echo "  Zuerst ausführen: ${SCRIPT_DIR}/build-mac.sh --arch ${ARCH}"
>>>>>>> 137763b (Prepare Module Manager macOS build)
    exit 1
fi

mkdir -p "$DIST_DIR"

# ── Methode 1: create-dmg (brew install create-dmg) ──────────
if command -v create-dmg &>/dev/null; then
    echo "  Nutze create-dmg ..."
    create-dmg \
        --volname "${APP_NAME}" \
        --window-pos 200 120 \
        --window-size 600 400 \
        --icon-size 100 \
        --icon "${APP_NAME}.app" 175 190 \
        --hide-extension "${APP_NAME}.app" \
        --app-drop-link 425 190 \
        "${DMG_PATH}" \
        "$APP_PATH"

# ── Methode 2: hdiutil (immer verfügbar, kein Hintergrund) ───
else
    echo "  create-dmg nicht gefunden — nutze hdiutil (einfaches DMG)."
    echo "  Für schönere DMGs: brew install create-dmg"
    echo ""

    TMP_DIR=$(mktemp -d)
    cp -r "$APP_PATH" "$TMP_DIR/"
    # Symlink auf /Applications für Drag-to-install
    ln -s /Applications "${TMP_DIR}/Applications"

    hdiutil create \
        -volname "${APP_NAME}" \
        -srcfolder "$TMP_DIR" \
        -ov \
        -format UDZO \
        "${DMG_PATH}"

    rm -rf "$TMP_DIR"
fi

echo ""
echo "  ✓ DMG fertig: ${DMG_PATH}"
echo ""

# ── Notarisierung (optional, nur mit Apple Developer Account) ─
echo "  ℹ  Für Notarisierung (Verteilung außerhalb App Store):"
echo "     xcrun notarytool submit \"${DMG_PATH}\" \\"
echo "         --apple-id \"deine@email.de\" \\"
echo "         --team-id \"DEIN_TEAM_ID\" \\"
echo "         --password \"app-specific-password\" \\"
echo "         --wait"
echo ""
