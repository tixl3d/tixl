# TiXL documentation

TiXL is a visual programming environment for motion graphics, live visuals, and creative coding on Windows. These pages are the source of truth for what it does and how to use it — if something is explained in Discord or at a meet-up, it should eventually end up here.

New? Start with the **[Welcome](getting-started/Welcome.md)** page, which points you at whichever entry fits you best.

## [Getting started](getting-started/README.md)

Understand what TiXL is, whether it fits your project, and how to load your first scene.

- [Welcome](getting-started/Welcome.md)
- [What is TiXL](getting-started/WhatIsTiXL.md)
- [System requirements](getting-started/SystemRequirements.md)
- [Introduction — a guided tour](getting-started/Introduction.md)
- [How TiXL works](getting-started/HowTixlWorks.md)
- [Concepts](getting-started/Concepts.md)
- [Video tutorials](getting-started/VideoTutorials.md)
- [Skill Quest (in-app tutorials)](getting-started/SkillQuest.md)
- [Migrating from Tooll3](getting-started/MigratingFromTooll3.md)
- [Reporting bugs and suggestions](getting-started/ReportBugs.md)
- [Community](getting-started/Community.md)

## [Install](install/README.md)

- [Installation](install/Installation.md) — Windows
- [Install on Linux](install/InstallLinux.md)
- [Install on macOS](install/InstallMacOS.md)
- [Set up a development environment](install/InstallDev.md)

## [Using TiXL](using/README.md)

Day-to-day reference: UI windows, graphs, connecting data, exporting, and live-use workflows.

- **Timeline and media:** [Timeline](using/Timeline.md) · [Export videos and image sequences](using/ExportVideos.md) · [Export as a standalone executable](using/ExportExecutables.md)
- **Presets and performance:** [Presets and snapshots](using/PresetsAndSnapshots.md) · [Live performances](using/LivePerformances.md) · [Sharing example projects](using/SharingExampleProjects.md)
- **Rendering and perf:** [Real-time rendering](using/RealtimeRendering.md) · [Optimizing performance](using/OptimizingRenderingPerformance.md) · [Remove static background](using/RemoveStaticBackground.md)
- **Connecting data:** [Sending and receiving OSC](using/OSC.md) · [ArtNet / DMX](using/ArtnetAndDMX.md)
- **General:** [Keyboard shortcuts](using/KeyboardShortcuts.md) · [Backups](using/Backups.md) · [FAQ](using/FAQ.md) · [FAQ: building content](using/FaqBuildingContent.md)

The [section README](using/README.md) lists topics that still need pages — graph-window reference, essential operators, recipes, MIDI / NDI / Spout, project structuring, and more.

## [Advanced](advanced/README.md)

- [Writing C# operators](advanced/WritingCodeOps.md) · [Creating new operators](advanced/CreatingNewOps.md) · [FAQ: writing C# operators](advanced/FaqDevOps.md)
- [Using custom shader operators](advanced/UsingCustomShaders.md) · [Shader development example](advanced/ShaderDevelopmentExample.md) · [Converting raymarching functions](advanced/ConvertSDFs.md)
- [Adding new fonts](advanced/AddingFonts.md) · [Single-line SVG fonts](advanced/SvgLineFonts.md)

## Operator reference

Every operator in the `Lib.*` namespace has a reference page generated from its in-editor description. See **[operator reference](operators/)** once the exporter is live.

## [Contributing](contributing/README.md)

Docs, bug reports, and contributing to TiXL itself — entry points and where developer docs live.
