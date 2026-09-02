# Using TiXL

Reference and how-tos for the day-to-day work: the UI, the graph, connecting inputs, exporting, and keeping projects organized.

## User interface

- [Timeline](Timeline.md) — keyframes, time clips, time warping.
- [Audio](Audio.md) — timeline clips, routing and effects, audio-reactivity, export.
- [Presets and snapshots](PresetsAndSnapshots.md) — variations, blending, live previews.
- [Keyboard shortcuts](KeyboardShortcuts.md) — the default map.

*Still to write:* dedicated pages for the **Graph window**, **Parameter window**, **Output window**, and **Settings / configuration**. These exist conceptually in [Introduction](../getting-started/Introduction.md) but aren't broken out yet.

## Working with graphs

- [Character animation](CharacterAnimation.md) — play glTF animation clips, blend and
  retarget poses, rig procedural geometry to skeletons.

*Still to write:*

- **Essential operators** — the 20 – 30 ops you'll use in almost any project, grouped by purpose (image, 3D, flow, text, IO).
- **Recipes** — "how do I do X" in TiXL (parallax, particle trails, VHS glitch, text reveal, audio-reactive color, camera shake, …). A cookbook.
- **Structuring projects** — namespaces, where to put your assets, reusing operators across projects.
- [FAQ: building content](FaqBuildingContent.md) — current FAQ on sub-patches, variables, flow control, particles.

## Connecting inputs

- [Sending and receiving OSC](OSC.md)
- [Controlling stage lights via ArtNet / DMX](ArtnetAndDMX.md)
- [Importing MIDI files](MidiFileImport.md) — load `.mid` files as timeline clips and sample notes / CC curves.

*Still to write:*

- **MIDI controllers** — `[MidiInput]`, learn mode, mapping a controller's physical layout to snapshots.
- **Spout** — send and receive textures between Windows apps.
- **NDI** — send and receive video over the network.

## Rendering and export

- [Exporting videos and image sequences](ExportVideos.md)
- [Exporting as a standalone executable](ExportExecutables.md)
- [Real-time rendering](RealtimeRendering.md) — concepts for artists (mip-maps, depth, blending, …).
- [Optimizing rendering performance](OptimizingRenderingPerformance.md) — when things slow down.
- [Removing static backgrounds in video inputs](RemoveStaticBackground.md)

## Live use and data management

- [TiXL for VJ and live performances](LivePerformances.md)
- [Using backups](Backups.md)
- [Sharing example projects](SharingExampleProjects.md)
- [FAQ](FAQ.md)

*Still to write:*

- **Resource management** — images, videos, shaders: where they live, how TiXL finds them, what to commit to git.
- **Collaboration workflows** — working with others on the same TiXL project over git.
