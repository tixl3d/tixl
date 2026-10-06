#!/usr/bin/env bash
#
# Installs what TiXL needs that it cannot bundle: the .NET SDK, the Vulkan loader and the Slang
# shader compiler. A stopgap until distribution packages exist - see DEPENDENCIES.md for the table
# this script is derived from, and aur/PKGBUILD for the Arch package.
#
# Run it as yourself, NOT with sudo: it calls sudo only for the package manager, so that slangc lands
# in your home directory rather than root's.
#
#   ./install-dependencies.sh            check, then ask before installing
#   ./install-dependencies.sh --check    report only, change nothing
#   ./install-dependencies.sh --yes      don't ask

set -uo pipefail

SLANG_VERSION="2026.18"
SLANG_DIR="$HOME/.local/opt/slang-$SLANG_VERSION"
DOTNET_MAJOR="10"

mode="ask"
case "${1:-}" in
    --check) mode="check" ;;
    --yes)   mode="yes" ;;
    "")      ;;
    *)       echo "Unknown option: $1"; exit 2 ;;
esac

if [ "$(id -u)" -eq 0 ]; then
    echo "Please run this as your normal user, not with sudo."
    echo "It will ask for sudo only where a package manager is needed; slangc installs into your home."
    exit 1
fi

# ---------------------------------------------------------------- distribution

family="other"
if [ -r /etc/os-release ]; then
    . /etc/os-release
    for id in ${ID:-} ${ID_LIKE:-}; do
        case "$id" in
            arch)           family="arch";   break ;;
            debian|ubuntu)  family="debian"; break ;;
            fedora|rhel)    family="fedora"; break ;;
            nixos)          family="nixos";  break ;;
        esac
    done
fi
echo "Distribution: ${PRETTY_NAME:-unknown}  (package family: $family)"

# ---------------------------------------------------------------- what's missing

missing_packages=()
install_slang="no"

# Note: no "grep -q" inside these pipelines. It exits on the first match, the writer upstream takes
# SIGPIPE, and under "set -o pipefail" a successful match would be reported as a failed check.

have_dotnet_sdk() {
    command -v dotnet >/dev/null 2>&1 || return 1
    local sdks
    sdks="$(dotnet --list-sdks 2>/dev/null)" || return 1
    awk -F. -v want="$DOTNET_MAJOR" '{ if ($1+0 >= want) found=1 } END { exit !found }' <<<"$sdks"
}

have_vulkan_loader() {
    local libraries
    libraries="$( { /sbin/ldconfig -p || ldconfig -p; } 2>/dev/null )"
    [ -n "$libraries" ] && [[ "$libraries" == *"libvulkan.so.1"* ]]
}

have_slangc() {
    [ -x "$SLANG_DIR/bin/slangc" ] || [ -n "${TIXL_SLANGC:-}" ] || command -v slangc >/dev/null 2>&1
}

case "$family" in
    arch)   dotnet_pkg="dotnet-sdk";                vulkan_pkg="vulkan-icd-loader" ;;
    debian) dotnet_pkg="dotnet-sdk-${DOTNET_MAJOR}.0"; vulkan_pkg="libvulkan1" ;;
    fedora) dotnet_pkg="dotnet-sdk-${DOTNET_MAJOR}.0"; vulkan_pkg="vulkan-loader" ;;
    *)      dotnet_pkg=""; vulkan_pkg="" ;;
esac

if have_dotnet_sdk; then
    printf "  %-18s found\n" ".NET ${DOTNET_MAJOR} SDK"
else
    printf "  %-18s MISSING\n" ".NET ${DOTNET_MAJOR} SDK"
    missing_packages+=("$dotnet_pkg")
fi

if have_vulkan_loader; then
    printf "  %-18s found\n" "Vulkan loader"
else
    printf "  %-18s MISSING\n" "Vulkan loader"
    missing_packages+=("$vulkan_pkg")
fi

if have_slangc; then
    printf "  %-18s found\n" "slangc"
else
    printf "  %-18s MISSING\n" "slangc"
    install_slang="yes"
fi

# Drop empties left by an unknown distribution.
packages=()
for p in ${missing_packages+"${missing_packages[@]}"}; do
    [ -n "$p" ] && packages+=("$p")
done

if [ ${#packages[@]} -eq 0 ] && [ "$install_slang" = "no" ]; then
    echo
    echo "Everything TiXL needs is already installed."
    exit 0
fi

if [ "$family" = "nixos" ]; then
    echo
    echo "On NixOS, add these to your configuration instead of installing them imperatively:"
    echo "  dotnetCorePackages.sdk_${DOTNET_MAJOR}_0, vulkan-loader, shader-slang"
    echo "  and hardware.graphics.enable = true;"
    exit 0
fi

if [ "$family" = "other" ] && [ ${#packages[@]} -gt 0 ]; then
    echo
    echo "Unknown distribution - install these yourself, then re-run:"
    echo "  .NET ${DOTNET_MAJOR} SDK and the Vulkan loader (see DEPENDENCIES.md)"
    [ "$install_slang" = "yes" ] && echo "slangc can still be installed below."
fi

echo
if [ "$mode" = "check" ]; then
    exit 1
fi

if [ "$mode" = "ask" ]; then
    [ ${#packages[@]} -gt 0 ] && echo "Will install with the package manager (needs sudo): ${packages[*]}"
    [ "$install_slang" = "yes" ] && echo "Will download slangc $SLANG_VERSION into $SLANG_DIR (no sudo)"
    read -r -p "Install now? [Y/n] " answer
    case "$answer" in
        [nN]*) echo "Nothing installed."; exit 1 ;;
    esac
fi

# ---------------------------------------------------------------- install

status=0

if [ ${#packages[@]} -gt 0 ]; then
    case "$family" in
        arch)   sudo pacman -S --needed "${packages[@]}" || status=1 ;;
        debian) sudo apt-get update && sudo apt-get install -y "${packages[@]}" || status=1 ;;
        fedora) sudo dnf install -y "${packages[@]}" || status=1 ;;
    esac
fi

if [ "$install_slang" = "yes" ]; then
    case "$(uname -m)" in
        x86_64)          asset="slang-$SLANG_VERSION-linux-x86_64-glibc-2.27.tar.gz" ;;
        aarch64|arm64)   asset="slang-$SLANG_VERSION-linux-aarch64-glibc-2.28.tar.gz" ;;
        *)               echo "No Slang build for $(uname -m)."; exit 1 ;;
    esac

    url="https://github.com/shader-slang/slang/releases/download/v$SLANG_VERSION/$asset"
    staging="$(mktemp -d)"
    trap 'rm -rf "$staging"' EXIT

    echo "Downloading $url"
    if curl -fsSL "$url" -o "$staging/$asset" && tar -xzf "$staging/$asset" -C "$staging"; then
        # Swap at the end, so a failed download cannot leave a half-populated install behind.
        rm -rf "$SLANG_DIR"
        mkdir -p "$(dirname "$SLANG_DIR")"
        mv "$staging" "$SLANG_DIR"
        trap - EXIT
        echo "slangc $("$SLANG_DIR/bin/slangc" -v 2>&1 | head -1) installed in $SLANG_DIR"
    else
        echo "Could not download or unpack Slang."
        status=1
    fi
fi

echo
if [ $status -eq 0 ]; then
    echo "Done. Start TiXL again."
else
    echo "Some steps failed - see the messages above."
fi
exit $status
