#!/usr/bin/env bash
# Build the FFmpeg shared libraries bundled with the macOS app: an LGPL build (no GPL/nonfree
# components, matching the license the shipped Windows/Linux builds enforce) of the 7.1 series
# whose avcodec-61 the Sdcb.FFmpeg 7.0.0 bindings require.
#
# The dylibs land in the directory given as the only argument, flat, named like Homebrew's
# (libavcodec.61.dylib, ...) - which is where FfmpegLibrary looks, next to the Video package's
# VideoServices.dll. Homebrew builds stay GPL, so without this script the app has no usable
# FFmpeg outside development machines (TIXL_FFMPEG_ALLOW_RESTRICTED).
#
# Encoders match the shipped Windows/Linux set (software: OpenH264, kvazaar, VPX, AV1 via
# SVT, FFV1, ProRes, HAP) plus Apple's VideoToolbox hardware H.264/HEVC, which is a system
# framework and LGPL-clean. avdevice brings the avfoundation demuxer for camera input.
# (MP3 *encoding* via libmp3lame is omitted: TiXL's export audio is AAC through FFmpeg's native
# encoder, MP3 decoding needs no external library, and Homebrew's lame 4.0 build doesn't satisfy
# FFmpeg's libmp3lame probe.)
#
# Environment:
#   TIXL_FFMPEG_VERSION   FFmpeg release to build (default 7.1.5 - the same 7.1.5 series the Linux
#                         bundle ships; 7.1.1 predates the SVT-AV1 4.x API and fails to compile
#                         against Homebrew's svt-av1)
set -euo pipefail

version="${TIXL_FFMPEG_VERSION:-7.1.5}"
out_dir="${1:?usage: build-ffmpeg.sh <output-directory>}"

brew_packages=(pkg-config openh264 kvazaar libvpx aom svt-av1 opus)
if ! brew list --versions "${brew_packages[@]}" >/dev/null 2>&1; then
    echo "Installing FFmpeg build dependencies (Homebrew bottles, no compilation)..."
    brew install --quiet "${brew_packages[@]}"
fi

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

echo "Fetching FFmpeg $version..."
curl -sSfL "https://ffmpeg.org/releases/ffmpeg-$version.tar.xz" -o "$work_dir/ffmpeg.tar.xz"
tar -xJf "$work_dir/ffmpeg.tar.xz" -C "$work_dir"
source_dir="$work_dir/ffmpeg-$version"

# Shared, non-GPL, libraries only - the editor drives libav* in-process. VideoToolbox/AudioToolbox
# are Apple system frameworks and stay. The prefix only feeds make install; the dylibs are copied
# out, so their absolute install names never resolve at runtime: FfmpegLibrary preloads every
# library by full path in dependency order, and dyld matches the dependencies against the images
# already in the process.
echo "Configuring FFmpeg (LGPL, shared)..."
make -C "$source_dir" distclean >/dev/null 2>&1 || true
configure_flags=(
    --prefix="$work_dir/install"
    --enable-shared
    --disable-static
    --disable-programs
    --disable-doc
    --disable-debug
    --disable-gpl
    --disable-nonfree
    --enable-libopenh264
    --enable-libkvazaar
    --enable-libvpx
    --enable-libaom
    --enable-libsvtav1
    --enable-libopus
    --enable-videotoolbox
    --enable-audiotoolbox
    # Homebrew's keg paths are not in the compiler's default search order; the explicit flags also
    # cover FFmpeg's direct compile-and-link probes for the dependencies pkg-config can't resolve.
    --extra-cflags=-I/opt/homebrew/include
    --extra-ldflags=-L/opt/homebrew/lib
)
( cd "$source_dir" && ./configure "${configure_flags[@]}" )

echo "Building FFmpeg ($(sysctl -n hw.ncpu) cores)..."
make -C "$source_dir" -j"$(sysctl -n hw.ncpu)" >/dev/null
make -C "$source_dir" install >/dev/null

mkdir -p "$out_dir"
installed=0
for dylib in "$work_dir/install/lib"/*.dylib; do
    # Skip the unversioned symlink; ship the versioned dylib itself, renamed to the short soname
    # (libavcodec.61.dylib) that FfmpegLibrary's filename lookups expect.
    if [[ -L "$dylib" ]]; then
        continue
    fi
    name="$(basename "$dylib" | sed -E 's/\.[0-9]+\.[0-9]+\.dylib$/.dylib/')"
    cp "$dylib" "$out_dir/$name"
    installed=$((installed + 1))
done

expected=(libavcodec libavdevice libavfilter libavformat libavutil libswresample libswscale)
for lib in "${expected[@]}"; do
    if ! compgen -G "$out_dir/$lib.*.dylib" >/dev/null; then
        echo "FFmpeg build did not produce $lib - not bundling." >&2
        exit 1
    fi
done

echo "Bundled $installed FFmpeg dylibs into $out_dir"
