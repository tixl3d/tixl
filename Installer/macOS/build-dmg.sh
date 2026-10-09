#!/usr/bin/env bash
# Build TiXL for Apple Silicon, wrap it in an app bundle and pack a DMG.
#
# Without a signing identity the bundle is signed ad hoc, which Apple Silicon requires to run at all. Gatekeeper
# then blocks the first launch; testers allow it once under System Settings > Privacy & Security > Open Anyway.
# With a Developer ID the bundle is signed for distribution and, given notary credentials, notarized, so it
# opens without that step.
#
# Usage:
#   Installer/macOS/build-dmg.sh                # build + bundle + DMG
#   Installer/macOS/build-dmg.sh --skip-build   # bundle + DMG from an existing Release build
#   Installer/macOS/build-dmg.sh --bundle-only  # build + bundle, no DMG
#
# Environment:
#   TIXL_SIGN_IDENTITY   Developer ID Application identity, e.g. "Developer ID Application: Name (TEAMID)".
#                        Unset or "-" signs ad hoc.
#   TIXL_NOTARY_PROFILE  A notarytool keychain profile (xcrun notarytool store-credentials) - or the three below,
#   TIXL_NOTARY_KEY      as used in CI: path to an App Store Connect API key (.p8),
#   TIXL_NOTARY_KEY_ID   its key ID,
#   TIXL_NOTARY_ISSUER   and its issuer ID.
set -euo pipefail

skip_build=false
bundle_only=false
for arg in "$@"; do
    case "$arg" in
        --skip-build) skip_build=true ;;
        --bundle-only) bundle_only=true ;;
        *) echo "Unknown option: $arg" >&2; exit 2 ;;
    esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$script_dir/../.." && pwd)"
version="$(sed -n 's:.*<TixlVersion>\(.*\)</TixlVersion>.*:\1:p' "$root/Tixl.props")"
rid="osx-arm64"
build_dir="$root/Editor/bin/Release/net10.0"
name="TiXL-$version-$rid"
output="$script_dir/Output"
app="$output/TiXL.app"
contents="$app/Contents"

# The Editor lives in Resources/TiXL, not in MacOS: signing seals Contents/MacOS as code and takes every folder
# with a dot in its name for a nested bundle - NuGet's runtimes/*/lib/net10.0 folders are full of them. In
# Resources the same files are sealed as data. MacOS holds only a launcher that starts the real executable.
payload="$contents/Resources/TiXL"

# The Vulkan loader and MoltenVK ship inside the bundle; a development machine has them from Homebrew or the
# LunarG SDK. Both depend on system libraries only, so they can be copied as they are.
vulkan_lib_dir="${VULKAN_LIB_DIR:-/opt/homebrew/lib}"

if [[ "$skip_build" == false ]]; then
    echo "Restoring dependencies..."
    dotnet restore "$root/t3.sln" -p:EnableWindowsTargeting=true -p:TixlEditorRuntime="$rid"

    # The Editor's CopyPlayer target picks the Player up from this folder.
    echo "Publishing Player (self-contained, $rid)..."
    rm -rf "$root/Player/bin/ReleasePublished"
    dotnet publish "$root/Player/Player.csproj" -c Release -r "$rid" --self-contained \
        -p:PublishDir="$root/Player/bin/ReleasePublished/" -p:EnableWindowsTargeting=true

    echo "Building solution in Release mode..."
    dotnet build "$root/t3.sln" -c Release --no-restore -p:EnableWindowsTargeting=true -p:TixlEditorRuntime="$rid"
fi

if [[ ! -x "$build_dir/TiXL" ]]; then
    echo "No Release build at $build_dir - run without --skip-build first." >&2
    exit 1
fi

echo "Bundling $app..."
rm -rf "$app"
mkdir -p "$contents/MacOS" "$payload"

# Everything the Editor finds relative to itself stays next to its executable, as in the Linux tarball.
cp -a "$build_dir/." "$payload/"

# Same split as the other installers: package metadata ships from the source tree.
for package in Lib Examples; do
    mkdir -p "$payload/Operators/$package/.meta"
    cp -a "$root/Operators/$package/.meta/." "$payload/Operators/$package/.meta/"
done

find "$payload" \( -name '*.proxy.mov' -o -name '*.waveform.png' -o -name '*.waveform.jpg' \) -delete

# Keep only the native runtimes this machine can load. Universal binaries are published as plain "osx".
while IFS= read -r -d '' runtime_dir; do
    case "$(basename "$runtime_dir")" in
        "$rid" | osx | unix) ;;
        *) rm -rf "$runtime_dir" ;;
    esac
done < <(find "$payload" -mindepth 2 -type d -path '*/runtimes/*' -prune -print0)

# The flat Dependencies copy also brings Windows-native DLLs; managed assemblies must stay.
while IFS= read -r -d '' dll; do
    description="$(file -b "$dll")"
    if [[ "$description" == PE32* && "$description" != *"Mono/.Net assembly"* ]]; then
        rm -f "$dll"
    fi
done < <(find "$payload" -name '*.dll' -print0)

if [[ ! -f "$payload/libbass.dylib" ]]; then
    echo "Warning: libbass.dylib is missing - audio won't work. See Dependencies/osx/README.md." >&2
fi

# Vulkan: the loader next to the executable (VulkanLoader looks there first), and MoltenVK registered through
# an ICD file the launcher names in VK_DRIVER_FILES.
if [[ -f "$vulkan_lib_dir/libvulkan.1.dylib" && -f "$vulkan_lib_dir/libMoltenVK.dylib" ]]; then
    cp -L "$vulkan_lib_dir/libvulkan.1.dylib" "$payload/"
    cp -L "$vulkan_lib_dir/libMoltenVK.dylib" "$payload/"
    mkdir -p "$contents/Resources/vulkan/icd.d"
    cat > "$contents/Resources/vulkan/icd.d/MoltenVK_icd.json" <<'JSON'
{
    "file_format_version": "1.0.0",
    "ICD": {
        "library_path": "../../TiXL/libMoltenVK.dylib",
        "api_version": "1.4.0",
        "is_portability_driver": true
    }
}
JSON
else
    echo "Warning: no Vulkan loader/MoltenVK in $vulkan_lib_dir - the app needs them installed. Set VULKAN_LIB_DIR." >&2
fi

# Icon: AppIcon.png is the logo on Apple's icon grid (a rounded square spanning 824 of 1024 px, with a shadow) -
# a full-bleed square looks oversized next to every other app and covers its name in the app switcher.
iconset="$output/TiXL.iconset"
rm -rf "$iconset"
mkdir -p "$iconset"
source_icon="$script_dir/AppIcon.png"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$source_icon" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$source_icon" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$contents/Resources/TiXL.icns"
rm -rf "$iconset"

cat > "$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>               <string>TiXL</string>
    <key>CFBundleDisplayName</key>        <string>TiXL</string>
    <key>CFBundleIdentifier</key>         <string>app.tixl.TiXL</string>
    <key>CFBundleExecutable</key>         <string>TiXL</string>
    <key>CFBundleIconFile</key>           <string>TiXL</string>
    <key>CFBundlePackageType</key>        <string>APPL</string>
    <key>CFBundleShortVersionString</key> <string>$version</string>
    <key>CFBundleVersion</key>            <string>$version</string>
    <key>LSMinimumSystemVersion</key>     <string>14.0</string>
    <key>LSApplicationCategoryType</key>  <string>public.app-category.graphics-design</string>
    <key>NSHighResolutionCapable</key>    <true/>
    <key>NSDocumentsFolderUsageDescription</key>
    <string>TiXL keeps your projects in your Documents folder.</string>
    <key>NSMicrophoneUsageDescription</key>
    <string>TiXL can react to sound from a microphone or line input.</string>
</dict>
</plist>
PLIST

cp "$root/LICENSE.txt" "$contents/Resources/LICENSE.txt"

# The launcher: the app's executable as far as macOS is concerned. It hands over to the Editor with exec, so the
# Editor keeps the launcher's process and with it the app's identity in the Dock.
cat > "$contents/MacOS/TiXL" <<'LAUNCHER'
#!/bin/sh
resources="$(cd "$(dirname "$0")/../Resources" && pwd)"
export VK_DRIVER_FILES="${VK_DRIVER_FILES:-$resources/vulkan/icd.d/MoltenVK_icd.json}"
exec "$resources/TiXL/TiXL" "$@"
LAUNCHER
chmod +x "$contents/MacOS/TiXL"

# Apple Silicon refuses unsigned native code; an ad-hoc signature satisfies that without an identity. Every
# Mach-O file gets its own, then the app seals the rest as resources. A Developer ID signature for notarization
# adds the hardened runtime and a secure timestamp to every file, and the entitlements to the executables.
identity="${TIXL_SIGN_IDENTITY:--}"
sign_options=(--force --sign "$identity")
executable_options=()
if [[ "$identity" != "-" ]]; then
    echo "Signing as $identity..."
    sign_options+=(--options runtime --timestamp)
    executable_options=(--entitlements "$script_dir/TiXL.entitlements")
else
    echo "Signing ad hoc..."
fi

find "$app" -name .DS_Store -delete
xattr -cr "$app"
libraries=()
executables=()
while IFS= read -r -d '' file; do
    description="$(file -b "$file")"
    case "$description" in
        *"Mach-O"*executable*) executables+=("$file") ;;
        *"Mach-O"*) libraries+=("$file") ;;
    esac
done < <(find "$payload" -type f \( -name '*.dylib' -o -name '*.so' -o -perm -u+x \) -print0)

if (( ${#libraries[@]} > 0 )); then
    printf '%s\0' "${libraries[@]}" \
        | xargs -0 -P 8 -n 32 codesign "${sign_options[@]}" 2>&1 | { grep -v "replacing existing signature" || true; }
fi
for file in ${executables[@]+"${executables[@]}"}; do
    codesign "${sign_options[@]}" ${executable_options[@]+"${executable_options[@]}"} "$file" 2>&1 | { grep -v "replacing existing signature" || true; }
done
codesign "${sign_options[@]}" ${executable_options[@]+"${executable_options[@]}"} "$contents/MacOS/TiXL"
codesign "${sign_options[@]}" ${executable_options[@]+"${executable_options[@]}"} "$app"
codesign --verify --strict "$app"

if [[ "$bundle_only" == true ]]; then
    echo "Bundled at $app"
    exit 0
fi

echo "Packing $name.dmg..."
dmg_root="$output/dmg"
rm -rf "$dmg_root" "$output/$name.dmg"
mkdir -p "$dmg_root"
cp -a "$app" "$dmg_root/"
ln -s /Applications "$dmg_root/Applications"
hdiutil create -volname "TiXL $version" -srcfolder "$dmg_root" -ov -format UDZO "$output/$name.dmg" >/dev/null
rm -rf "$dmg_root"

if [[ "$identity" != "-" ]]; then
    codesign --force --sign "$identity" --timestamp "$output/$name.dmg"

    notary_options=()
    if [[ -n "${TIXL_NOTARY_PROFILE:-}" ]]; then
        notary_options=(--keychain-profile "$TIXL_NOTARY_PROFILE")
    elif [[ -n "${TIXL_NOTARY_KEY:-}" ]]; then
        notary_options=(--key "$TIXL_NOTARY_KEY" --key-id "$TIXL_NOTARY_KEY_ID" --issuer "$TIXL_NOTARY_ISSUER")
    fi

    if (( ${#notary_options[@]} > 0 )); then
        # Apple scans the upload and, once accepted, the ticket is stapled so the DMG opens offline too.
        echo "Notarizing (this takes a few minutes)..."
        xcrun notarytool submit "$output/$name.dmg" "${notary_options[@]}" --wait
        xcrun stapler staple "$output/$name.dmg"
    else
        echo "Signed but not notarized: no notary credentials given."
    fi
fi

echo "Done: $output/$name.dmg ($(du -h "$output/$name.dmg" | cut -f1))"
