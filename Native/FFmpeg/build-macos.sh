#!/usr/bin/env bash
# Builds the LGPL FFmpeg shared libraries that the macOS DMG bundles next to the Video package and packs them
# into ffmpeg-<version>-lgpl-macos-arm64.tar.gz. Needs Apple's SDK, so it has to run on a Mac; CI runs it in
# ffmpeg-macos.yml, which attaches the archive to the deps-ffmpeg-<version> release.
#
# Homebrew's FFmpeg is GPL, and FfmpegLibrary refuses GPL builds. This one links nothing but what macOS ships
# (VideoToolbox, AudioToolbox, zlib, bzip2, iconv); --disable-autodetect keeps configure from picking up
# whatever else Homebrew left on the build machine.
#
# Developers can unpack the archive anywhere and point TIXL_FFMPEG_DIR at it to run against the shipped build.
#
# Usage: build-macos.sh [output-dir]    (default: Native/FFmpeg/Output)
set -euo pipefail

# FFmpeg 7.1 has the avutil 59 / avcodec 61 ABI that Sdcb.FFmpeg 7.0.0 binds to. Sdcb loads the libraries by
# major-versioned name (libavutil.59.dylib), so a new FFmpeg major also needs new bindings and new names below.
version=7.1.5
sha256=de668509caf9e35e3cd162473441fdb29538c6d96ed080292b3cf9e6fc5d558f
libraries=(libavutil.59 libswresample.5 libswscale.8 libavcodec.61 libavformat.61 libavfilter.10 libavdevice.61)
min_macos=14.0 # LSMinimumSystemVersion in Installer/macOS/build-dmg.sh

if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
    echo "This script builds for Apple Silicon and must run on an arm64 Mac." >&2
    exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$script_dir/Output}"
mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
work="$output_dir/work"
source_archive="$output_dir/downloads/ffmpeg-$version.tar.xz"
source_url="https://ffmpeg.org/releases/ffmpeg-$version.tar.xz"

if [[ ! -f "$source_archive" ]]; then
    echo "Downloading FFmpeg $version..."
    mkdir -p "$(dirname "$source_archive")"
    curl -fsSL -o "$source_archive.part" "$source_url"
    mv "$source_archive.part" "$source_archive"
fi
if [[ "$(shasum -a 256 "$source_archive" | cut -d' ' -f1)" != "$sha256" ]]; then
    echo "Checksum mismatch for $source_archive" >&2
    exit 1
fi

rm -rf "$work"
mkdir -p "$work"
tar -xJf "$source_archive" -C "$work"
source_dir="$work/ffmpeg-$version"

# No --enable-gpl, --enable-version3 or --enable-nonfree: the result is LGPL-2.1-or-later.
# --install-name-dir makes every library find its siblings through @loader_path wherever the bundle puts them.
configure_flags=(
    --prefix="$work/install"
    --arch=arm64
    --cc=clang
    --enable-shared
    --disable-static
    --disable-programs
    --disable-doc
    --disable-debug
    --disable-autodetect
    --enable-videotoolbox
    --enable-audiotoolbox
    --enable-zlib
    --enable-bzlib
    --enable-iconv
    --install-name-dir=@loader_path
    --extra-cflags="-mmacosx-version-min=$min_macos"
    --extra-ldflags="-mmacosx-version-min=$min_macos"
)
(
    cd "$source_dir"
    ./configure "${configure_flags[@]}"
    make -j"$(sysctl -n hw.ncpu)"
    make install
)

for flag in CONFIG_GPL CONFIG_NONFREE; do
    if ! grep -q "^#define $flag 0$" "$source_dir/config.h"; then
        echo "$flag is set; the build would not be LGPL." >&2
        exit 1
    fi
done

stage="$work/stage"
mkdir -p "$stage"
for library in "${libraries[@]}"; do
    file="$work/install/lib/$library.dylib"
    if [[ ! -f "$file" ]]; then
        echo "Missing $library.dylib. Did the FFmpeg major version change?" >&2
        exit 1
    fi
    # Copy the file the major-versioned symlink points to, under the name Sdcb asks for.
    cp -L "$file" "$stage/"
done

# Every bundled library must be arm64, target the app's minimum macOS, identify itself through @loader_path,
# and depend on nothing outside the bundle except the OS (a stray /opt/homebrew path would break on users' Macs).
for file in "$stage"/*.dylib; do
    name="$(basename "$file")"
    if [[ "$(lipo -archs "$file")" != arm64 ]]; then
        echo "$name is not arm64-only: $(lipo -archs "$file")" >&2
        exit 1
    fi
    minos="$(otool -l "$file" | awk '/minos/ { print $2; exit }')"
    if [[ "$minos" != "$min_macos" ]]; then
        echo "$name targets macOS $minos instead of $min_macos" >&2
        exit 1
    fi
    if [[ "$(otool -D "$file" | tail -n +2)" != "@loader_path/$name" ]]; then
        echo "$name has install name $(otool -D "$file" | tail -n +2)" >&2
        exit 1
    fi
    foreign="$(otool -L "$file" | tail -n +2 | awk '{ print $1 }' \
        | grep -v -e '^@loader_path/' -e '^/usr/lib/' -e '^/System/Library/' || true)"
    if [[ -n "$foreign" ]]; then
        echo "$name depends on libraries outside the bundle and the OS:" >&2
        echo "$foreign" >&2
        exit 1
    fi
done

# The LGPL obligations for shared libraries: the licence text and the exact source with its build recipe.
cp "$source_dir/COPYING.LGPLv2.1" "$source_dir/LICENSE.md" "$stage/"
cat > "$stage/SOURCE.txt" <<SOURCE
FFmpeg $version, LGPL-2.1-or-later, built by TiXL's Native/FFmpeg/build-macos.sh.

Source:  $source_url
SHA-256: $sha256

Configured with:
  ./configure ${configure_flags[*]}
SOURCE

archive="$output_dir/ffmpeg-$version-lgpl-macos-arm64.tar.gz"
tar -czf "$archive" -C "$stage" .
(cd "$output_dir" && shasum -a 256 "$(basename "$archive")" > "$(basename "$archive").sha256")
echo "Built $archive ($(du -h "$archive" | cut -f1))"
