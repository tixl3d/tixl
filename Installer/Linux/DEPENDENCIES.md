# TiXL Linux Dependencies

What TiXL needs on a Linux system, for packagers and for users of the portable tarball.
`ldd` over the staged tarball found the "always present" libraries below. The rest are loaded at
runtime (`dlopen`), which `ldd` can't see.

Package names: Arch, AUR and nixpkgs names were checked against the live repositories (2026-10).
Debian/Ubuntu and Fedora names follow those distros' conventions but haven't been tested yet.

Until distribution packages exist, `install-dependencies.sh` installs the required components from this
table for Arch, Debian/Ubuntu and Fedora. Run it as your normal user — it calls `sudo` only for the package
manager, and installs slangc into your home only where the download doesn't bring it:

```bash
Installer/Linux/install-dependencies.sh --check   # report only
Installer/Linux/install-dependencies.sh           # ask, then install
```

## Required

At startup the Editor checks for the Vulkan loader and slangc, and shows the install command
for the user's distribution when one is missing. The tarball brings slangc along; without it, the Editor can
download it itself, since that one needs no package manager.

| Component | Why | Arch | Debian / Ubuntu | Fedora | nixpkgs |
|---|---|---|---|---|---|
| **Vulkan loader** | Rendering | `vulkan-icd-loader` | `libvulkan1` | `vulkan-loader` | `vulkan-loader` |
| **Vulkan driver** | Rendering | `vulkan-radeon` / `vulkan-intel` / `nvidia-utils` | `mesa-vulkan-drivers` (NVIDIA: proprietary driver) | `mesa-vulkan-drivers` (NVIDIA: proprietary driver) | `hardware.graphics.enable = true;` |
| **ALSA library** | Audio, where no PipeWire or PulseAudio runs (loaded at runtime) | `alsa-lib` | `libasound2t64` (older: `libasound2`) | `alsa-lib` | `alsa-lib` |
| **ICU, OpenSSL** | .NET globalization and HTTPS (loaded by the bundled runtime) | `icu`, `openssl` | `libicu*`, `libssl3` | `libicu`, `openssl-libs` | `icu`, `openssl` |
| **X11 or Wayland client libs, libGL/EGL** | Windows (SDL3), startup dialog (GLFW) | present on any desktop | present on any desktop | present on any desktop | present on any desktop |

**Always present** on desktop Linux (found by `ldd`): glibc 2.38 or newer (the prebuilt `libcimgui.so` from
ImGui.NET needs it; everything else needs 2.34), libstdc++,
zlib, fontconfig, freetype, libpng, expat, bzip2, brotli.

A distribution's Slang package follows Slang's releases, not TiXL's pinned line, so the startup check would
report it as the wrong version. A package that deliberately ships its distribution's slangc sets
`TIXL_SLANGC_UNPINNED=1` (next to `TIXL_SLANGC`): the version then goes to the log instead of a dialog, and
the shader cache is keyed on the version in use so blobs from two compilers stay apart. Shaders can still
compile differently — that is what the pin is about — so prefer the pinned line where it is available.

## Optional

| Component | Enables | Arch | Debian / Ubuntu | Fedora | nixpkgs |
|---|---|---|---|---|---|
| NDI runtime (`libndi.so.6`) | Ndi operators and NDI output streams. The Ndi extension ships it; without that extension it is found via `NDI_RUNTIME_DIR_V6` or the system library path. Source discovery needs `avahi-daemon` | AUR `ndi-sdk` | [NDI runtime](http://ndi.link/NDIRedistV6) | [NDI runtime](http://ndi.link/NDIRedistV6) | `ndi` (unfree) |
| .NET 10 SDK | Compiling projects that use NuGet packages, embedded resources or custom build steps. The editor compiles all other projects itself and names the reason when it needs the SDK | `dotnet-sdk` | `dotnet-sdk-10.0` (Ubuntu archive or `ppa:dotnet/backports`) | `dotnet-sdk-10.0` | `dotnetCorePackages.sdk_10_0` |
| Vulkan validation layers | GPU debugging | `vulkan-validation-layers` | `vulkan-validationlayers` | `vulkan-validation-layers` | `vulkan-validation-layers` |

## Bundled in the tarball

These ship with TiXL, so packagers don't need to provide them. Distro packages may swap in the system
version where one exists.

| Component | Comes from | License |
|---|---|---|
| .NET runtime (Editor and Player; `--system-dotnet` leaves it out of the Editor) | Microsoft | MIT |
| slangc 2026.18 (`slang/`; `--system-slang` leaves it out for packages that depend on their distribution's Slang, found via `TIXL_SLANGC` or `PATH`) | shader-slang GitHub release | Apache-2.0 WITH LLVM-exception |
| SDL3 | `ppy.SDL3-CS` NuGet | Zlib |
| SkiaSharp, HarfBuzzSharp | NuGet | MIT / BSD-3-Clause |
| FFmpeg 7 (`libavcodec.so.61`, ...) | Video operator package | LGPL-3.0 |
| ONNX Runtime | NuGet | MIT |
| cimgui, GLFW | NuGet | MIT / Zlib |

License texts for everything bundled are in `Dependencies/licenses/`.

## Not available on Linux yet

These are feature gaps, not packaging issues. The affected operators are expected to fail when used.

- **Emgu CV** (`cvextern`) and **Mediapipe**: only Windows natives are vendored.
- **Spout**: Windows-only by design.

## Build dependencies

.NET 10 SDK, `git`, `bash`, `file`, `tar`, and network access to
NuGet. See `build-tarball.sh` and `.github/workflows/linux-build.yml`.
