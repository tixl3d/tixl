---
video: asiY0Jo1oJw
type: update
date: 2024-03-17
title: Tooll V3.9 / New Features
duration: 0:15:05
---

Release-notes walkthrough of Tooll 3.9: expanded operator/parameter docs, faster video+audio export, glTF scene loading, Ableton Link sync, colored/stretchable points, the combined bias-and-gain control, new effects, and particle-system updates.

## Mentions
- 1:04→1:19 [ui:ParameterWindow] · explained · scripted · Concept · 80% — Per-parameter help docs now surface on hover over the help icon; clicking the icon toggles a dedicated help mode.
- 2:01→2:34 [ui:RenderSettings] · explained · scripted · Concept · 78% — Video export gained audio output, no pre-render format conversion, and an auto-increment version-number checkbox so you can re-hit render with the panel docked to save numbered work steps.
- 2:34→3:11 [LoadGltfScene] · explained · scripted · Concept · 85% — Loads complete glTF scenes (not just single meshes), auto-combining textures into the correct channel maps for physically-based rendering; expand the scene setup to inspect each node and its material textures.
- 3:11→3:42 [DrawScene] · explained · scripted · Parameters · 82% — Renders a loaded glTF scene and exposes overrides for the file's roughness and metallic values to tweak surface appearance.
- 3:42→3:55 [DrawMeshAtPoints] [UseMaterial] · explained · scripted · Example · 75% — To fold an imported scene into the mesh-effects pipeline, take the scene's separate mesh and material outputs: feed the mesh to a point-instancing draw and wire a material operator into the material slot.
- 4:01→5:03 [AbletonLinkSync] · in-depth · scripted · Concept · 88% — Syncs to any Ableton Link source on the network (Live, Traktor, Bitwig, TouchDesigner, VVVV, DJ gear); unlike MIDI it shares bar-phase, not just BPM, so bar-based timing locks perfectly.
- 4:57→5:06 [AnimValue] [Blob] · passing · scripted · Example · 65% — Driving a periodic value into a metaball-style radius demonstrates beat-synced animation once a network clock source is connected.
- 5:06→5:50 [RandomizePoints] · explained · scripted · Concept · 82% — Adds per-point colors and scaling to an instanced point set; its random seed is a float, so sweeping it yields smooth deterministic animation rather than discrete reshuffles.
- 5:36→5:43 [RadialPoints] · passing · scripted · Example · 70% — Source of a ring of points that gets noise-displaced and used to scatter mesh instances.
- 6:00→6:21 [DrawBillboards] · explained · scripted · Parameters · 72% — Renders per-point camera-facing sprites; a random-face parameter (among many others) picks which cell of a texture each billboard shows, useful with point colors over a video texture.
- 6:21→6:48 [LinePoints] · explained · scripted · Example · 75% — Combining two line sources — one with start/end colors, one slid through a color gradient — then layering a noise offset whose amount you crank via the infinity slider, since leaving point counts unmatched lets the offset bite.
- 6:34→6:48 [ui:InfinitySlider] · passing · scripted · Tip · 70% — Drag-anywhere control for pushing a parameter far past its normal range without a fixed maximum.
- 7:02→7:14 [Remap] · passing · scripted · Concept · 68% — Among the operators that adopted the combined bias-and-gain control for reshaping a value's response curve.
- 7:14→8:25 [ui:ParameterPopup] · in-depth · scripted · Concept · 80% — The combined bias-and-gain curve acts like a tone curve: an S-shape adds contrast (gain), an inverse S flattens it; bias pushes values brighter or darker — one Vec2 parameter covers both shapes across images, distributions and easing.
- 8:25→9:08 [ui:ParameterWindow] · explained · scripted · Tip · 78% — Enabling the Vec2 control in the parameter settings lets you drag both components of a paired parameter at once and see the resulting distribution/weighting curve live.
- 8:50→9:08 [PerlinNoise] [FractalNoise] [Tint] [LinePoints] [RadialPoints] · passing · scripted · Concept · 70% — Listed among operators that gained the bias-and-gain control to reshape their output distribution or response.
- 9:15→9:28 [NdiInput] · explained · scripted · Performance · 80% — Performance overhaul lets it handle a wide range of source formats and up to 4K footage reliably.
- 9:28→9:39 [AdvancedFeedback2] · explained · scripted · Concept · 80% — Successor to [AdvancedFeedback] shipping presets that produce fluid-like feedback looks.
- 9:39→10:07 [Dither] · explained · scripted · Concept · 80% — Adds a retro dithered look; exposes blend modes plus bias-and-gain, exemplifying few-controls/wide-range effect design.
- 10:07→10:12 [WorleyNoise] [ShardNoise] · passing · scripted · Concept · 60% — Two new noise variants added to the noise toolkit.
- 10:12→10:18 [FractalNoise] · passing · scripted · Concept · 65% — Cleaned-up and updated in this release.
- 10:18→10:37 [Tint] [RemapColor] · explained · scripted · Concept · 75% — Paired color-adjustment operators that benefit from bias-and-gain; the larger one now does color cycling for generated effects.
- 10:37→11:01 [ParticleSystem] · explained · scripted · Concept · 82% — Recap of the minimal setup: emit event points, add a force, render the buffer as points.
- 11:01→11:33 [ParticleSystem] · in-depth · scripted · Gotcha · 80% — Lifetime reworked: a default of -1 derives the max lifetime from emitted-point count and buffer length, and particle age written to the w attribute is now normalized 0–1 so you can drive color or scale over a particle's life.
- 11:27→11:40 [DrawMeshAtPoints2] · passing · scripted · Parameters · 70% — Offers many options for scaling instanced geometry by a per-point attribute such as normalized particle age.
- 11:40→12:37 [TextureMapForce] · explained · scripted · Parameters · 62% — Accelerates particles along a connected normal map (which must use signed-normal mode); a twist parameter rotates the sampled direction — 180° reverses it — and a confine-depth parameter keeps particles within a visible distance from the camera.
- 12:37→13:06 [SnapToAnglesForce] · explained · scripted · Concept · 80% — Nudges particle velocities toward a set of defined angles, producing clean grid-like structures.
- 13:11→13:16 [ui:OperatorSettings] · passing · scripted · Tip · 65% — Input parameters can now be renamed directly from the editor.
- 13:16→13:26 [ui:ControlBar] · passing · scripted · Tip · 60% — Beat-tapping gained shortcuts: Z to tap, X to resync to measure start.
- 13:26→13:35 [ui:FocusMode] · passing · scripted · Tip · 70% — Switching between foreground and background control by clicking the white edge is now easier.
- 13:35→13:41 [ui:Settings] · passing · scripted · Tip · 70% — Devices can be re-scanned from the settings window without restarting the app.
- 13:41→13:51 · passing · scripted · Tip · 65% — A new Multiply-Alpha blend mode added to most image operators enables powerful masking.
- 13:51→14:07 [ui:GradientEditor] · explained · scripted · Tip · 72% — Gradient editing now supports undo/redo, and evenly distributing steps before interpolation now includes the final step.
- 14:28→14:42 [ui:Graph] · passing · scripted · Concept · 70% — Roadmap preview: the magnetic-graph system shown last year is slated for integration to make patching snappier and more live.
