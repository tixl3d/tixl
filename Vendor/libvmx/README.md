# libvmx (native VMX codec for Open Media Transport)

The video codec libomtnet compresses with. It ships per platform in `Operators/Omt/dependencies/runtimes/<rid>/native/`
and is loaded by `OmtRuntime` (Omt package) by full path. License: MIT, see `Operators/Omt/dependencies/libvmx-LICENSE.txt`.

| Runtime | File | Origin |
| --- | --- | --- |
| `win-x64`, `win-arm64` | `libvmx.dll` | Official upstream release, 1.0.0.19 |
| `osx` | `libvmx.dylib` | Official upstream release (universal x86_64 + arm64) |
| `linux-x64`, `linux-arm64` | `libvmx.so` | Built with `build-linux.sh` from upstream commit `31724bb` |

## Why Linux is built here

A plain build on a current distribution links that distribution's libstdc++ (`GLIBCXX_3.4.32` on GCC 13), which
older distributions such as Ubuntu 22.04 or Debian 12 can't load. Upstream's script also compiles every file with
`-mavx2`, which lets the compiler use AVX2 outside the runtime-dispatched path, so a CPU without AVX2 would crash on
an illegal instruction rather than take the SSE path.

`build-linux.sh` therefore:

- targets glibc 2.17 with Zig's toolchain and a statically linked libc++ (needs only `libc`, `libm`, `libpthread`,
  `libdl`),
- compiles only `vmxcodec_avx2.cpp` with AVX2/BMI2/LZCNT and the rest for SSE4.1,
- force-includes `portable_bits.h` there, which replaces `__lzcnt64`/`_tzcnt_u64` with zero-safe builtins. The decoder
  relies on `lzcnt(0) == 64`; on a CPU without LZCNT that instruction silently runs as `BSR` and returns something
  else, which would corrupt the decode.

The resulting bitstream is identical to a build with upstream's own script (same frame encoded with both and compared byte for byte, on x64 and on arm64 under emulation).
