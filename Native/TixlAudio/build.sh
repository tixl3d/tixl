#!/usr/bin/env bash
# Builds libtixlaudio.so into Dependencies/linux-x64. Run after changing anything in this folder.
# Run-time linking (miniaudio's default) dlopens PulseAudio/ALSA/JACK, so nothing links against them here.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$script_dir/../../Dependencies/linux-x64}"
mkdir -p "$output_dir"
cc -std=c99 -O2 -fPIC -shared -s -fvisibility=hidden \
    -D_GNU_SOURCE -Wall -Wno-unused-function -Wno-unused-variable \
    -o "$output_dir/libtixlaudio.so" "$script_dir/tixl_audio.c" \
    -lm -lpthread -ldl
