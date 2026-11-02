#!/usr/bin/env bash
# Builds libtixlaudio.dylib into Dependencies/osx. Needs Apple's SDK, so it has to run on a Mac.
# miniaudio loads CoreAudio at runtime by default, so the frameworks are only needed for their headers.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$script_dir/../../Dependencies/osx}"
mkdir -p "$output_dir"
cc -std=c99 -O2 -fPIC -dynamiclib -fvisibility=hidden \
    -o "$output_dir/libtixlaudio.dylib" "$script_dir/tixl_audio.c" \
    -framework CoreFoundation -framework CoreAudio -framework AudioToolbox
