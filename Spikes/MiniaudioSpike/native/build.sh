#!/usr/bin/env bash
# Builds the native audio library (miniaudio + stb_vorbis + the TiXL wrapper) for Linux.
# Usage: build.sh <output-directory>
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$script_dir/bin}"
mkdir -p "$output_dir"

# Run-time linking (miniaudio's default) dlopens PulseAudio/ALSA/JACK, so nothing links against them here.
cc -std=c99 -O2 -g -fPIC -shared -fvisibility=hidden \
    -D_GNU_SOURCE -Wall -Wno-unused-function -Wno-unused-variable \
    -o "$output_dir/libtixlaudio.so" "$script_dir/tixl_audio.c" \
    -lm -lpthread -ldl
