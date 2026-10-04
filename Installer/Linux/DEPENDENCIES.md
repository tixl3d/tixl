# TiXL Linux Dependencies

What TiXL needs on a Linux system, for packagers and for users of the portable tarball.
`ldd` over the staged tarball found the "always present" libraries below. The rest are loaded at
runtime (`dlopen`), which `ldd` can't see.

Package names: Arch, AUR and nixpkgs names were checked against the live repositories (2026-10).
Debian/Ubuntu and Fedora names follow those distros' conventions but haven't been tested yet.

## Required

At startup the Editor checks for the .NET SDK, the Vulkan loader and slangc, and shows the install command
for the user's distribution when one is missing.

| Component | Why | Arch | Debian / Ubuntu | Fedora | nixpkgs |
|---|---|---|---|---|---|
| **.NET 10 SDK** (not just the runtime) | Runs the Editor and compiles operator projects at runtime (`dotnet restore` / `dotnet build`, needs network for NuGet) | `dotnet-sdk` | `dotnet-sdk-10.0` (Ubuntu archive or `ppa:dotnet/backports`; Debian via packages.microsoft.com) | `dotnet-sdk-10.0` | `dotnetCorePackages.sdk_10_0` |
| **Vulkan loader** | Rendering | `vulkan-icd-loader` | `libvulkan1` | `vulkan-loader` | `vulkan-loader` |
| **Vulkan driver** | Rendering | `vulkan-radeon` / `vulkan-intel` / `nvidia-utils` | `mesa-vulkan-drivers` (NVIDIA: proprietary driver) | `mesa-vulkan-drivers` (NVIDIA: proprietary driver) | `hardware.graphics.enable = true;` |
| **slangc** | Shader compilation. Found via `TIXL_SLANGC`, `~/.local/opt/slang-2026.18/bin`, or `PATH`. Tested with 2026.18 | AUR `shader-slang-bin` (note: `extra/slang` is the unrelated S-Lang) | GitHub release of shader-slang | GitHub release of shader-slang | `shader-slang` |
| **ALSA library** | Audio output (BASS loads it; PipeWire/PulseAudio provide the ALSA plugin) | `alsa-lib` | `libasound2t64` (older: `libasound2`) | `alsa-lib` | `alsa-lib` |
| **ICU, OpenSSL** | .NET globalization and HTTPS | pulled in by the SDK package | pulled in by the SDK package | pulled in by the SDK package | pulled in by the SDK package |
| **X11 or Wayland client libs, libGL/EGL** | Windows (SDL3), startup dialog (GLFW) | present on any desktop | present on any desktop | present on any desktop | present on any desktop |

**Always present** on desktop Linux (found by `ldd`): glibc 2.38 or newer (the prebuilt `libcimgui.so` from
ImGui.NET needs it; everything else needs 2.34), libstdc++,
zlib, fontconfig, freetype, libpng, expat, bzip2, brotli.

## Optional

| Component | Enables | Arch | Debian / Ubuntu | Fedora | nixpkgs |
|---|---|---|---|---|---|
| NDI runtime (`libndi.so.6`) | Ndi operators | AUR `ndi-sdk` | vendor download (ndi.video) | vendor download | `ndi` (unfree) |
| Vulkan validation layers | GPU debugging | `vulkan-validation-layers` | `vulkan-validationlayers` | `vulkan-validation-layers` | `vulkan-validation-layers` |

## Bundled in the tarball

These ship with TiXL, so packagers don't need to provide them. Distro packages may swap in the system
version where one exists.

| Component | Comes from | License |
|---|---|---|
| .NET runtime (Editor and Player; `--system-dotnet` leaves it out of the Editor) | Microsoft | MIT |
| SDL3 | `ppy.SDL3-CS` NuGet | Zlib |
| SkiaSharp, HarfBuzzSharp | NuGet | MIT / BSD-3-Clause |
| FFmpeg 7 (`libavcodec.so.61`, ...) | Video operator package | LGPL-3.0 |
| ONNX Runtime | NuGet | MIT |
| cimgui, GLFW | NuGet | MIT / Zlib |
| **BASS, BASSmix, BASSFLAC** | un4seen.com (not in git, fetched at build time, see `Dependencies/linux-x64/README.md`) | **Proprietary, free for non-commercial use** |

License texts for everything bundled are in `Dependencies/licenses/`.

## Not available on Linux yet

These are feature gaps, not packaging issues. The affected operators are expected to fail when used.

- **Emgu CV** (`cvextern`) and **Mediapipe**: only Windows natives are vendored.
- **Live audio input**: BASSWASAPI is Windows-only.
- **Spout**: Windows-only by design.

## Build dependencies

.NET 10 SDK, `git`, `bash`, `file`, `tar`, `unzip` (for the BASS archives), and network access to
NuGet. See `build-tarball.sh` and `.github/workflows/linux-build.yml`.
