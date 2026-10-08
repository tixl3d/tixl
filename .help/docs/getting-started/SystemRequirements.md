# System requirements

TiXL runs on Windows 10 and 11, natively on Linux as a preview, and natively on Apple Silicon Macs as an
early preview. This page lists what each system needs; [Platform support](../install/PlatformSupport.md)
lists which features work where.

## Windows

- **OS:** Windows 10 (64-bit) or Windows 11.
- **GPU:** a DirectX 11.3-compatible card. GeForce GTX 970 or later is the minimum target; RTX-class cards
  handle complex shader graphs much more comfortably.
- **DirectX:** 11.3 runtime and the Windows Graphics Tools. The installer handles both.

## Linux

- **CPU:** 64-bit x86 (`x86_64`). ARM machines aren't supported.
- **GPU:** a Vulkan driver — Mesa for AMD and Intel, or NVIDIA's proprietary driver.
- **Distribution:** from late 2023 or later, for example Ubuntu 24.04, Debian 13, Fedora 39, Linux Mint 22 or a
  current Arch.
- The Slang shader compiler; TiXL offers to download it.

See [Install on Linux](../install/InstallLinux.md) for the details per distribution.

## macOS

- **Mac:** Apple Silicon (M1 or newer). Intel Macs aren't supported.
- **OS:** macOS 14 Sonoma or newer.
- The Slang shader compiler; TiXL offers to download it.

The graphics chip shares the Mac's memory, so plan for more memory than on a PC with a separate graphics
card. See [Install on macOS](../install/InstallMacOS.md).

## On every system

- **.NET 10 SDK**, not just the runtime: TiXL compiles operators while it runs. The Windows installer
  includes it; on Linux and macOS you install it once.
- **RAM:** 16 GB comfortable. Large video assets push memory use up quickly.
- **Disk:** about 600 MB for TiXL itself. Projects with video and image assets grow fast — keep those on a
  fast SSD.
- **Display:** the editor UI is usable on 1920×1080 but prefers more screen real estate. A second display for
  full-screen output is strongly recommended for live use.

## For developing C# operators

- Visual Studio 2022, Visual Studio Code or JetBrains Rider (free for non-commercial use). On Linux and macOS,
  Rider or Visual Studio Code.
- The .NET 10 SDK — the same one TiXL needs to run.

See [Set up a development environment](../install/InstallDev.md) for the full walk-through.
