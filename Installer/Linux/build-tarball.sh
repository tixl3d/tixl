#!/usr/bin/env bash
# Build a portable Linux release of TiXL and pack it as a tarball.
# Distro packages (AUR, Flatpak, Nix) can reuse the staged folder instead of re-deriving the layout.
#
# Usage:
#   Installer/Linux/build-tarball.sh               # build + stage + pack
#   Installer/Linux/build-tarball.sh --skip-build  # stage + pack an existing Release build
#   Installer/Linux/build-tarball.sh --stage-only  # build + stage, no tarball (for distro packages)
#   Installer/Linux/build-tarball.sh --system-dotnet  # don't bundle the .NET runtime with the editor
#   Installer/Linux/build-tarball.sh --system-slang   # don't bundle slangc (the package depends on the distro's)
set -euo pipefail

skip_build=false
stage_only=false
system_dotnet=false
system_slang=false
for arg in "$@"; do
    case "$arg" in
        --skip-build) skip_build=true ;;
        --stage-only) stage_only=true ;;
        --system-dotnet) system_dotnet=true ;;
        --system-slang) system_slang=true ;;
        *) echo "Unknown option: $arg" >&2; exit 2 ;;
    esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$script_dir/../.." && pwd)"
version="$(sed -n 's:.*<TixlVersion>\(.*\)</TixlVersion>.*:\1:p' "$root/Tixl.props")"
rid="linux-x64"
build_dir="$root/Editor/bin/Release/net10.0"
name="tixl-$version-$rid"
stage="$script_dir/Output/$name"

# The editor bundles the .NET runtime unless a distro package provides it.
editor_runtime_args=(-p:TixlEditorRuntime="$rid")
if [[ "$system_dotnet" == true ]]; then
    editor_runtime_args=()
fi

if [[ "$skip_build" == false ]]; then
    echo "Restoring dependencies..."
    dotnet restore "$root/t3.sln" -p:EnableWindowsTargeting=true "${editor_runtime_args[@]}"

    # The Editor's CopyPlayer target picks the Player up from this folder.
    echo "Publishing Player (self-contained, $rid)..."
    rm -rf "$root/Player/bin/ReleasePublished"
    dotnet publish "$root/Player/Player.csproj" -c Release -r "$rid" --self-contained \
        -p:PublishDir="$root/Player/bin/ReleasePublished/" -p:EnableWindowsTargeting=true

    echo "Building solution in Release mode..."
    dotnet build "$root/t3.sln" -c Release --no-restore -p:EnableWindowsTargeting=true "${editor_runtime_args[@]}"
fi

if [[ ! -x "$build_dir/TiXL" ]]; then
    echo "No Release build at $build_dir - run without --skip-build first." >&2
    exit 1
fi

echo "Staging $name..."
rm -rf "$stage"
mkdir -p "$stage"
cp -a "$build_dir/." "$stage/"

# Same split as the Windows installer: package metadata ships from the source tree.
for package in Lib Examples; do
    mkdir -p "$stage/Operators/$package/.meta"
    cp -a "$root/Operators/$package/.meta/." "$stage/Operators/$package/.meta/"
done

find "$stage" \( -name '*.proxy.mov' -o -name '*.waveform.png' -o -name '*.waveform.jpg' \) -delete

# Keep only the native runtimes this platform can load (Editor, Player and every operator package).
while IFS= read -r -d '' runtime_dir; do
    case "$(basename "$runtime_dir")" in
        "$rid" | unix) ;;
        *) rm -rf "$runtime_dir" ;;
    esac
done < <(find "$stage" -mindepth 2 -type d -path '*/runtimes/*' -prune -print0)

# The flat Dependencies copy also brings Windows-native DLLs; managed assemblies must stay.
while IFS= read -r -d '' dll; do
    description="$(file -b "$dll")"
    if [[ "$description" == PE32* && "$description" != *"Mono/.Net assembly"* ]]; then
        rm -f "$dll"
    fi
done < <(find "$stage" -name '*.dll' -print0)

if [[ ! -f "$stage/libtixlaudio.so" ]]; then
    echo "Warning: libtixlaudio.so is missing - audio won't work. See Native/TixlAudio/README.md." >&2
fi

share="$stage/share"
install -Dm644 "$script_dir/common/app.tixl.TiXL.desktop"       "$share/applications/app.tixl.TiXL.desktop"
install -Dm644 "$script_dir/common/app.tixl.TiXL.metainfo.xml"  "$share/metainfo/app.tixl.TiXL.metainfo.xml"
install -Dm644 "$script_dir/common/app.tixl.TiXL.png"           "$share/icons/hicolor/256x256/apps/app.tixl.TiXL.png"
cp "$root/LICENSE.txt" "$stage/LICENSE.txt"
# Help > Third-Party Licenses opens this. Nix builds have no python3 and go without it.
if command -v python3 >/dev/null 2>&1; then
    python3 "$root/Installer/check-licences.py" --notices "$stage/THIRD-PARTY-NOTICES.txt" --platform "$rid"
else
    echo "Warning: python3 not found - the release has no THIRD-PARTY-NOTICES.txt." >&2
fi

# Slang: only what slangc needs for HLSL -> SPIR-V (~43 MB; slang-llvm alone is 150 MB and only serves CPU
# targets), the subset the Windows and macOS builds ship too. SlangShaderCompiler.FindCompiler looks in
# <app>/slang/bin, and SlangLibrary loads ../lib/libslang-compiler.so from there to compile in-process. The
# version comes from the compiler's pin; the checksum has to follow it by hand.
if [[ "$system_slang" == false ]]; then
    slang_version="$(sed -n 's/.*PinnedVersion = "\(.*\)";.*/\1/p' "$root/Core/Resource/ShaderCompiling/SlangShaderCompiler.cs")"
    slang_sha256="e45ea4f117d51b8c1e84fa49f562081e73a9f29d02bd4f7fad20678603282829" # 2026.18
    slang_archive="$script_dir/Output/downloads/slang-$slang_version-linux-x86_64-glibc-2.27.tar.gz"
    if [[ ! -f "$slang_archive" ]]; then
        echo "Downloading Slang $slang_version..."
        mkdir -p "$(dirname "$slang_archive")"
        curl -fsSL -o "$slang_archive.part" \
            "https://github.com/shader-slang/slang/releases/download/v$slang_version/$(basename "$slang_archive")"
        mv "$slang_archive.part" "$slang_archive"
    fi
    if [[ "$(sha256sum "$slang_archive" | cut -d' ' -f1)" != "$slang_sha256" ]]; then
        echo "Checksum mismatch for $slang_archive. After changing PinnedVersion, update slang_sha256 here." >&2
        exit 1
    fi

    slang_staging="$script_dir/Output/slang-staging"
    rm -rf "$slang_staging"
    mkdir -p "$slang_staging" "$stage/slang/bin" "$stage/slang/lib"
    tar -xzf "$slang_archive" -C "$slang_staging"
    cp "$slang_staging/bin/slangc" "$stage/slang/bin/"
    for library in "libslang-compiler.so.0.$slang_version" "libslang-glslang-$slang_version.so" \
                   "libslang-glsl-module-$slang_version.so"; do
        cp "$slang_staging/lib/$library" "$stage/slang/lib/"
    done
    ln -s "libslang-compiler.so.0.$slang_version" "$stage/slang/lib/libslang-compiler.so"
    cp "$slang_staging/LICENSE" "$stage/slang/"
    rm -rf "$slang_staging"
    if [[ "$("$stage/slang/bin/slangc" -v 2>&1)" != "$slang_version"* ]]; then
        echo "Bundled slangc does not report version $slang_version" >&2
        exit 1
    fi
fi

# Tarball users have no package manager pulling in Vulkan; this does it for them.
install -Dm755 "$script_dir/install-dependencies.sh" "$stage/install-dependencies.sh"
install -Dm644 "$script_dir/DEPENDENCIES.md"         "$stage/DEPENDENCIES.md"

# Packaging recipes (the Flatpak manifest) point at this instead of the versioned folder.
ln -sfn "$name" "$script_dir/Output/tixl-linux-x64"

if [[ "$stage_only" == true ]]; then
    echo "Staged at $stage"
    exit 0
fi

echo "Packing $name.tar.gz..."
tar -C "$script_dir/Output" -czf "$script_dir/Output/$name.tar.gz" "$name"
echo "Done: $script_dir/Output/$name.tar.gz ($(du -h "$script_dir/Output/$name.tar.gz" | cut -f1))"
