#!/usr/bin/env bash
# Cross-compiles tixlaudio.dll for Windows x64 from Linux with Zig's bundled clang and MinGW headers.
# miniaudio loads WASAPI and COM at runtime, so the DLL only imports the UCRT and KERNEL32.
# Usage: build-windows.sh <path-to-zig> <output-directory>
set -euo pipefail

zig="${1:?path to the zig executable}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${2:-$script_dir/bin-windows}"
mkdir -p "$output_dir"

"$zig" cc -target x86_64-windows-gnu -std=c99 -O2 -shared \
    -o "$output_dir/tixlaudio.dll" "$script_dir/tixl_audio.c"
