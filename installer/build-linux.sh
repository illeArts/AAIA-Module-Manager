#!/usr/bin/env bash
# ============================================================
#  AAIA Module Manager - Linux Build + Installer
#
#  Creates portable .tar.gz archives and Debian packages for
#  linux-x64 and/or linux-arm64.
#
#  Requirements:
#    - Linux or WSL build host
#    - .NET 8 SDK installed (https://dotnet.microsoft.com)
#    - aaia-sdk in sibling folder: ../aaia-sdk
#    - dpkg-deb for .deb output (optional, tar.gz is always built)
#
#  Usage:
#    chmod +x installer/build-linux.sh
#    ./installer/build-linux.sh [--arch x64|arm64|both] [--skip-deb]
# ============================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

APP_NAME="AAIA Module Manager"
APP_ID="aaia-module-manager"
VERSION="2.3.0"
EXECUTABLE="AAIA.ModuleManager"
PROJECT="${REPO_ROOT}/src/AAIA.ModuleManager/AAIA.ModuleManager.csproj"
SDK_PROJECT="${REPO_ROOT}/../aaia-sdk/src/AAIA.Shared.Contracts/AAIA.Shared.Contracts.csproj"
ICON_SRC="${REPO_ROOT}/src/AAIA.ModuleManager/Assets/AAIA_Module_Manager.png"
PUBLISH_ROOT="${REPO_ROOT}/publish"
DIST_DIR="${SCRIPT_DIR}/dist"

ARCH="x64"
BUILD_DEB="true"
DOTNET_CMD="${DOTNET_CMD:-dotnet}"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="${DOTNET_SYSTEM_GLOBALIZATION_INVARIANT:-1}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --arch)
            ARCH="${2:-}"
            shift 2
            ;;
        --skip-deb)
            BUILD_DEB="false"
            shift
            ;;
        *)
            echo "Unknown argument: $1"
            exit 1
            ;;
    esac
done

check_prerequisites() {
    if [[ "$(uname -s)" != "Linux" ]]; then
        echo "ERROR: Linux packages must be built on Linux or WSL."
        echo "This project selects its .NET target framework from the build OS."
        exit 1
    fi

    if ! command -v "$DOTNET_CMD" >/dev/null 2>&1; then
        echo "ERROR: dotnet was not found."
        echo "Install the .NET 8 SDK or set DOTNET_CMD to the dotnet executable."
        exit 1
    fi

    if [[ ! -f "$SDK_PROJECT" ]]; then
        echo "ERROR: aaia-sdk was not found at:"
        echo "  ${SDK_PROJECT}"
        echo ""
        echo "Expected folder layout:"
        echo "  AAIAGitHub/"
        echo "    aaia-module-manager/"
        echo "    aaia-sdk/"
        exit 1
    fi
}

deb_arch_for() {
    case "$1" in
        x64) echo "amd64" ;;
        arm64) echo "arm64" ;;
        *)
            echo "Unsupported architecture: $1" >&2
            exit 1
            ;;
    esac
}

build_arch() {
    local arch="$1"
    local runtime="linux-${arch}"
    local deb_arch
    local publish_dir="${PUBLISH_ROOT}/${runtime}"
    local package_name="AAIA_ModuleManager_v${VERSION}_${runtime}"
    local tar_path="${DIST_DIR}/${package_name}.tar.gz"

    deb_arch="$(deb_arch_for "$arch")"

    echo ""
    echo "============================================================"
    echo "  Building ${APP_NAME} for ${runtime}"
    echo "============================================================"

    echo ""
    echo "[1/4] dotnet restore"
    "$DOTNET_CMD" restore "$PROJECT"

    echo ""
    echo "[2/4] dotnet publish (Release / ${runtime} / self-contained)"
    rm -rf "$publish_dir"
    "$DOTNET_CMD" publish "$PROJECT" \
        -c Release \
        -f net8.0 \
        -r "$runtime" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o "$publish_dir"

    chmod +x "${publish_dir}/${EXECUTABLE}"

    echo ""
    echo "[3/4] portable archive"
    mkdir -p "$DIST_DIR"
    tar -C "$publish_dir" -czf "$tar_path" .
    echo "Created: ${tar_path}"

    if [[ "$BUILD_DEB" != "true" ]]; then
        echo ""
        echo "[4/4] skipped Debian package (--skip-deb)"
        return
    fi

    if ! command -v dpkg-deb >/dev/null 2>&1; then
        echo ""
        echo "[4/4] skipped Debian package (dpkg-deb not found)"
        echo "Install dpkg-dev/dpkg-deb or run this script on Debian/Ubuntu."
        return
    fi

    echo ""
    echo "[4/4] Debian package"

    local pkg_root="${TMPDIR:-/tmp}/${APP_ID}-deb-${runtime}"
    local deb_path="${DIST_DIR}/${package_name}.deb"
    rm -rf "$pkg_root"

    mkdir -p \
        "${pkg_root}/DEBIAN" \
        "${pkg_root}/opt/${APP_ID}" \
        "${pkg_root}/usr/bin" \
        "${pkg_root}/usr/share/applications" \
        "${pkg_root}/usr/share/pixmaps"

    cp -a "${publish_dir}/." "${pkg_root}/opt/${APP_ID}/"
    ln -s "/opt/${APP_ID}/${EXECUTABLE}" "${pkg_root}/usr/bin/${APP_ID}"

    if [[ -f "$ICON_SRC" ]]; then
        cp "$ICON_SRC" "${pkg_root}/usr/share/pixmaps/${APP_ID}.png"
    fi

    cat > "${pkg_root}/usr/share/applications/${APP_ID}.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=${APP_NAME}
Comment=Dev tool for AAIA module and plugin developers
Exec=/opt/${APP_ID}/${EXECUTABLE}
Icon=${APP_ID}
Terminal=false
Categories=Development;Utility;
StartupWMClass=AAIA.ModuleManager
EOF

    cat > "${pkg_root}/DEBIAN/control" <<EOF
Package: ${APP_ID}
Version: ${VERSION}
Section: devel
Priority: optional
Architecture: ${deb_arch}
Maintainer: Andre Iljaschow / IleArts
Homepage: https://aaiagent.de
Depends: libc6, libicu-dev, libfontconfig1, libfreetype6, libx11-6, libxcb1, libice6, libsm6, libgl1, libxkbcommon0, libxrandr2, libxi6, libxcursor1, libxinerama1
Description: AAIA Module Manager
 Dev tool for AAIA module and plugin developers.
EOF

    dpkg-deb --root-owner-group --build "$pkg_root" "$deb_path"
    echo "Created: ${deb_path}"
}

check_prerequisites

case "$ARCH" in
    both)
        build_arch "x64"
        build_arch "arm64"
        ;;
    x64|arm64)
        build_arch "$ARCH"
        ;;
    *)
        echo "Invalid architecture: ${ARCH} (allowed: x64, arm64, both)"
        exit 1
        ;;
esac

echo ""
echo "============================================================"
echo "  Linux build complete."
echo "  Output: ${DIST_DIR}"
echo "============================================================"
