#!/usr/bin/env bash
# Builds libvmx.so for linux-x64 and linux-arm64 into Operators/Omt/dependencies/runtimes/<rid>/native.
#
# Zig's toolchain targets glibc 2.17 and links its own libc++ statically, so the result loads on any glibc
# distribution from roughly 2014 on without a particular libstdc++ (a plain g++/clang++ build on a current distro
# needs that distro's libstdc++). On x64 only the runtime-dispatched AVX2 object is compiled with AVX2; the rest
# targets SSE4.1, so a CPU without AVX2 takes the SSE path instead of crashing on an illegal instruction.
#
# Requires: git, python3 with `pip install ziglang`, and binutils for both architectures (strip).
set -euo pipefail

LIBVMX_COMMIT=31724bb   # github.com/openmediatransport/libvmx, 2026-10-20
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../../Operators/Omt/dependencies/runtimes"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

git clone --quiet https://github.com/openmediatransport/libvmx.git "$WORK/libvmx"
git -C "$WORK/libvmx" checkout --quiet "$LIBVMX_COMMIT"
SRC="$WORK/libvmx/src"
ZIG="python3 -m ziglang c++"
FLAGS="-O3 -std=c++17 -fdeclspec -fPIC"

# linux-x64
cd "$WORK"
$ZIG -target x86_64-linux-gnu.2.17 $FLAGS -msse4.1 -mpopcnt -include "$HERE/portable_bits.h" -c "$SRC/vmxcodec.cpp" -o vmxcodec.o
$ZIG -target x86_64-linux-gnu.2.17 $FLAGS -msse4.1 -mpopcnt -include "$HERE/portable_bits.h" -c "$SRC/vmxcodec_x86.cpp" -o vmxcodec_x86.o
$ZIG -target x86_64-linux-gnu.2.17 $FLAGS -mavx2 -mbmi -mbmi2 -mlzcnt -c "$SRC/vmxcodec_avx2.cpp" -o vmxcodec_avx2.o
$ZIG -target x86_64-linux-gnu.2.17 -shared -fPIC -Wl,-soname,libvmx.so vmxcodec.o vmxcodec_x86.o vmxcodec_avx2.o -o libvmx-x64.so
strip --strip-unneeded libvmx-x64.so
mkdir -p "$OUT/linux-x64/native" && cp libvmx-x64.so "$OUT/linux-x64/native/libvmx.so"

# linux-arm64 (NEON path, as upstream's buildlinuxarm64.sh)
$ZIG -target aarch64-linux-gnu.2.17 $FLAGS -shared -Wl,-soname,libvmx.so "$SRC/vmxcodec_arm.cpp" "$SRC/vmxcodec.cpp" -o libvmx-arm64.so
aarch64-linux-gnu-strip --strip-unneeded libvmx-arm64.so
mkdir -p "$OUT/linux-arm64/native" && cp libvmx-arm64.so "$OUT/linux-arm64/native/libvmx.so"

echo "Built:"; ls -la "$OUT"/linux-*/native/libvmx.so
