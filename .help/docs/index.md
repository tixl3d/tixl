# TiXL documentation

# Introduction

Welcome to TiXL—your real-time visual playground. Whether you're crafting motion design or building interactive installations, what you create is what you see. There is no offline rendering, no preview modes, and no waiting for a final result. Instead, you work directly with the final output, shaping visuals as if you were sculpting them in real time.

TiXL combines procedural animation techniques—such as LFOs, sequencers, and event-based systems—with traditional keyframe animation. This allows you to move seamlessly between structured motion design workflows and fully interactive, real-time experiences. It bridges the gap between tools like After Effects and real-time environments used for installations and live visuals.

The system is built around a flexible operator-based workflow. You can create complex setups by combining and reusing components, organizing them into reusable structures, and building your own tools. While TiXL is powered by C#, you don’t need to write code to use it. Artists can work visually and intuitively, while developers can extend and customize the system deeply when needed.

TiXL is designed for real-time performance and runs primarily on the GPU. A modern graphics card will give you the best experience, enabling smooth playback and immediate feedback for even complex scenes. If in doubt, TiXL prioritizes usability first, then flexibility, and finally performance—ensuring that it remains approachable without sacrificing power.

Learning TiXL is designed to be straightforward. Interactive tutorials guide you step by step, supported by in-editor documentation, example setups, and a growing library of video tutorials. Whether you're just starting out or exploring advanced techniques, there are multiple paths to get you up to speed quickly.

## Getting Started

### Requirements

TiXL currently runs on Windows and performs best on systems with a dedicated graphics card. While it is possible to run it on [Linux](/install/InstallLinux.md) and [macOS](/install/InstallMacOS.md) through emulation, native versions for these platforms are in development.

[Installation on Windows](/install/Installation.md) is simple: download the installer, run it, and launch TiXL.

## First Steps

After installation, the most effective way to begin is to start the interactive *SkillQuest* tutorials from the main screen. These guided sessions walk you through the core concepts in a hands-on way, helping you understand how TiXL works by creating visuals directly.

From there, you can explore operators, modify existing setups, and gradually build your own projects.


## Pick your path

- Watch the [video tutorials](/getting-started/VideoTutorials.md). Start with the short intro, then follow the full playlist when you have 15 minutes.

- To understand the concepts and technology behind read [What is TiXL](/getting-started/WhatIsTiXL.md), then [How TiXL works](/getting-started/HowTixlWorks.md). The terms in [Concepts](/getting-started/Concepts.md) will appear everywhere.

## Community

If you have questions or feedback, the easiest way is to join us on Discord:

 [![Discord](https://img.shields.io/discord/823853172619083816.svg?style=for-the-badge)](https://discord.gg/YmSyQdeH3S). 
 
But the [Community](/getting-started/Community.md) is also active on GitHub, YouTube and in real life :-)

___

## Getting Started

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

Every operator in the `Lib.*` namespace has a reference page generated from its in-editor description. See **[operator reference](operators/index.md)** once the exporter is live.

## [Contributing](contributing/README.md)

Docs, bug reports, and contributing to TiXL itself — entry points and where developer docs live.
