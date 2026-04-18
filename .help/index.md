# TiXL documentation

TiXL is a visual programming environment for motion graphics, live visuals, and creative coding on Windows. These pages cover installation, the user interface, and the techniques you'll use once you're past "Hello world".

If this is your first time here, start with the **[Introduction](general/Introduction.md)** or watch the **[video tutorials](general/VideoTutorials.md)**. If something isn't working, the **[FAQ](general/FAQ.md)** covers the common pitfalls.

## Install TiXL

- [Installation](setup/Installation.md) — Windows (recommended)
- [Install on Linux](setup/InstallLinux.md) — under Wine
- [Install on macOS](setup/InstallMacOS.md) — under Sikarugir
- [Set up a development environment](setup/InstallDev.md) — run TiXL from Visual Studio or Rider

Coming from Tooll3? See [Migrating from Tooll3](general/MigratingFromTooll3.md).

## Learn the basics

- [Introduction](general/Introduction.md) — a guided tour
- [How TiXL works](general/HowTixlWorks.md) — the graph, caching, resolution
- [Concepts](general/Concepts.md) — operators, parameters, connections
- [Video tutorials](general/VideoTutorials.md)
- [Migrating from Tooll3](general/MigratingFromTooll3.md) — what changed in v4

## The user interface

- [Using the timeline](ui/TimeLine.md) — keyframes, time clips, time warping
- [Exporting videos and image sequences](ui/ExportVideos.md)
- [Keyboard shortcuts](ui/KeyboardShortcuts.md)
- [Presets and snapshots](ui/PresetsAndSnapshots.md)

## Make content

- [FAQ: building content](general/FaqBuildingContent.md)
- [TiXL for VJ and live performances](general/LivePerformances.md)
- [Using backups](general/Backups.md)
- [Sharing example projects](general/SharingExampleProjects.md)

## Advanced features

- [Using custom shader operators](advanced/UsingCustomShaders.md)
- [Writing C# operators](advanced/WritingCodeOps.md)
- [Creating new operators](advanced/CreatingNewOps.md)
- [FAQ: writing C# operators](advanced/FaqDevOps.md)
- [A shader development example](advanced/ShaderDevelopmentExample.md)
- [Converting raymarching functions](advanced/ConvertSDFs.md)
- [Real-time rendering for artists](advanced/RealtimeRendering.md)
- [Optimizing rendering performance](advanced/OptimizingRenderingPerformance.md)
- [Exporting as a standalone executable](advanced/ExportExecutables.md)
- [Controlling stage lights via ArtNet / DMX](advanced/ArtnetAndDMX.md)
- [Sending and receiving OSC](advanced/OSC.md)
- [Removing static backgrounds in video inputs](advanced/RemoveStaticBackground.md)
- [Adding new fonts](advanced/AddingFonts.md)
- [Creating and using single-line SVG fonts](advanced/SvgLineFonts.md)

## Operator reference

Every operator in the `Lib.*` namespace has a reference page generated from its in-editor description — see **[operator reference](operators/)**.

## Found a problem?

[Report an issue or suggest an improvement.](general/ReportBugs.md) Docs fixes are especially welcome — every page on this site is edited in [`.help/`](https://github.com/tixl3d/tixl/tree/main/.help) and there's an "Edit on GitHub" link at the top-right of every page.
