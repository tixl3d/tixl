# System requirements

TiXL runs on Windows 10 and 11. For Linux and macOS use one of the Wine-based setups linked under [install](../install/README.md).

## Hardware

- **GPU:** a DirectX 11.3-compatible card. GeForce GTX 970 or later is the minimum target; RTX-class cards handle complex shader graphs much more comfortably.
- **RAM:** 16 GB comfortable. Large video assets push memory use up quickly.
- **Disk:** installer ~500 MB. Projects with video and image assets grow fast — keep those on a fast SSD.
- **Display:** the editor UI is usable on 1920×1080 but prefers more screen real estate. A second display for full-screen output is strongly recommended for live use.

## Software

- **OS:** Windows 10 (64-bit) or Windows 11.
- **DirectX:** 11.3 runtime; Windows Graphics Tools. The installer handles both.
- **.NET 9 runtime.** Bundled with the installer.

## For developing C# operators

- Visual Studio Community 2022, or JetBrains Rider (free for non-commercial use).
- The .NET 9 **SDK** (not just the runtime).

See [Set up a development environment](../install/InstallDev.md) for the full walk-through.
