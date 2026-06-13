#!/bin/bash
# ============================================================
#  AAIA Module Manager — AppIcon.icns erstellen
#
#  Konvertiert eine PNG-Quelldatei (mind. 1024x1024) in ein
#  macOS .icns-File mit allen benötigten Größen.
#
#  Voraussetzungen:
#    - Xcode Command Line Tools: xcode-select --install
#    - Quelldatei: ein PNG mit 1024x1024 px (quadratisch)
#
#  Aufruf:
#    ./installer/create-icns.sh <pfad/zu/icon_1024.png>
#
#  Ausgabe:
#    installer/AppIcon.icns  (wird von build-mac.sh automatisch eingebettet)
# ============================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT="${SCRIPT_DIR}/AppIcon.icns"

# ── Eingabe prüfen ────────────────────────────────────────────
if [ -z "$1" ]; then
    echo ""
    echo "  Verwendung: ./installer/create-icns.sh <pfad/zu/icon_1024.png>"
    echo ""
    echo "  Erwartetes Format:"
    echo "    - PNG, 1024x1024 px, quadratisch"
    echo "    - Transparenter Hintergrund empfohlen"
    echo ""
    exit 1
fi

SOURCE_PNG="$1"

if [ ! -f "$SOURCE_PNG" ]; then
    echo "✗ Datei nicht gefunden: ${SOURCE_PNG}"
    exit 1
fi

# ── iconutil prüfen ───────────────────────────────────────────
if ! command -v iconutil &>/dev/null; then
    echo "✗ iconutil nicht gefunden."
    echo "  Installiere Xcode Command Line Tools: xcode-select --install"
    exit 1
fi

if ! command -v sips &>/dev/null; then
    echo "✗ sips nicht gefunden (sollte auf jedem Mac vorhanden sein)."
    exit 1
fi

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   AAIA Module Manager  —  ICNS erstellen     ║"
echo "╚══════════════════════════════════════════════╝"
echo ""
echo "  Quelle: ${SOURCE_PNG}"
echo "  Ziel:   ${OUTPUT}"
echo ""

# ── Iconset-Ordner anlegen ────────────────────────────────────
ICONSET_DIR=$(mktemp -d)/AppIcon.iconset
mkdir -p "$ICONSET_DIR"

# ── Alle benötigten Größen generieren ─────────────────────────
# macOS braucht: 16, 32, 64, 128, 256, 512, 1024 — je 1x und 2x (@2x)

generate_size() {
    local size=$1
    local filename=$2
    sips -z "$size" "$size" "$SOURCE_PNG" --out "${ICONSET_DIR}/${filename}" \
        > /dev/null 2>&1
    echo "  ✓ ${filename} (${size}x${size})"
}

generate_size   16  "icon_16x16.png"
generate_size   32  "icon_16x16@2x.png"
generate_size   32  "icon_32x32.png"
generate_size   64  "icon_32x32@2x.png"
generate_size  128  "icon_128x128.png"
generate_size  256  "icon_128x128@2x.png"
generate_size  256  "icon_256x256.png"
generate_size  512  "icon_256x256@2x.png"
generate_size  512  "icon_512x512.png"
generate_size 1024  "icon_512x512@2x.png"

# ── iconutil → .icns ─────────────────────────────────────────
echo ""
echo "  Kompiliere zu .icns ..."
iconutil -c icns "$ICONSET_DIR" -o "$OUTPUT"

# ── Aufräumen ─────────────────────────────────────────────────
rm -rf "$(dirname "$ICONSET_DIR")"

echo ""
echo "  ✓ Fertig: ${OUTPUT}"
echo ""
echo "  Nächster Schritt:"
echo "    ./installer/build-mac.sh --arch arm64"
echo ""
