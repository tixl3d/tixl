---
video: asiY0Jo1oJw
type: update
date: 2024-03-17
title: Tooll V3.9 — release overview: docs & help mode, faster export, glTF scenes, Ableton Link, colored points, bias-and-gain
duration: 15:05
---

A scripted narrated walkthrough of Tooll 3.9's headline additions: greatly expanded operator/parameter documentation and an in-context help mode, a 10x-faster video exporter with audio export, full glTF scene loading for PBR, Ableton Link sync, colored/stretchable points, the new combined Bias-and-Gain control, and a batch of new operators and effects. It closes with UI quality-of-life changes and a roadmap teaser (Vulkan, magnetic graph). Most segments are tight, demo-driven explanations rather than deep dives.

## Mentions
- 0:00→0:21 [] · passing · scripted · 100% — What Tooll3 is: an open-source realtime motion-graphics tool for animation, live shows, and installations.
- 0:44→1:19 [ui:ParameterWindow] · explained · scripted · 80% — How the expanded per-operator and per-parameter docs surface on hover and toggle into a help mode in the parameter window.
- 1:19→1:37 [ui:ParameterWindow] · explained · scripted · 78% — The cleaned-up parameter-window layout and a new documentation/operator style guide for consistency.
- 1:58→2:34 [ui:PlayerExporter] · explained · scripted · 70% — The faster video export (up to 10x), no-format-conversion rendering, audio export with motion blur, and the increment-version checkbox for docked repeated renders.
- 2:34→3:04 [LoadGltfScene] · explained · scripted · 88% — Loading complete glTF scenes (not just single meshes) with auto-combined PBR channel maps via the LoadGLTF-scene operator and its scene-setup nodes.
- 3:04→3:40 [DrawScene] · explained · scripted · 85% — Using DrawScene to render a loaded glTF scene and override roughness/metallic to tweak appearance.
- 3:40→3:56 [DrawMeshAtPoints] · explained · scripted · 70% — Feeding the glTF operator's mesh and material outputs into a draw-at-points + used-material setup to instance scene geometry.
- 4:08→4:57 [AbletonLinkSync] · in-depth · scripted · 95% — How the AbletonLinkSync operator syncs tempo and bar position from Live, Traktor, Bitwig, TouchDesigner, VVVV and DJ gear over the network.
- 4:57→5:07 [AnimValue][Blob] · explained · scripted · 75% — Driving a Blob radius with an AnimValue to get perfect beat-synced animation via Ableton Link.
- 5:07→5:34 [] · explained · scripted · 80% — The overhauled points data type now carrying color and stretch/scale, with many new operators to set, modify, and randomize them.
- 5:34→5:51 [RadialPoints][AddNoise][DrawMeshAtPoints] · explained · scripted · 70% — Example one: radial points plus noise used to instance meshes.
- 5:51→6:01 [RandomizePoints] · in-depth · scripted · 90% — Inserting RandomizePoints for per-instance color and scale, with a float random seed for deterministic animated variation.
- 6:01→6:21 [DrawBillboards] · explained · scripted · 78% — Example two: point colors on a video texture drawn with DrawBillboards, highlighting its random-face and other now-documented parameters.
- 6:21→6:35 [LinePoints] · explained · scripted · 72% — Example three: two LinePoints sets with start/end colors and a sliding color gradient.
- 6:35→6:56 [ui:InfinitySlider] · explained · scripted · 80% — Adding a noise offset and using the new infinity slider to crank a parameter beyond normal limits.
- 7:07→8:26 [] · in-depth · scripted · 55% — The new combined bias-and-gain control explained as an adjustment curve: gain reshapes contrast (S-curve), bias brightens/darkens, one parameter for images, distributions, and easing.
- 8:26→8:50 [ui:ParameterWindow] · explained · scripted · 70% — Enabling the Vec2 control in the parameter settings to drag bias and gain together with a live visual of curves and weights.
- 8:50→9:08 [Remap][PerlinNoise][RandomizePoints][FractalNoise][RemapColor][Tint][LinePoints][RadialPoints] · passing · scripted · 80% — Rundown of operators that now embed the bias-and-gain control.
- 9:08→9:29 [NdiInput] · explained · scripted · 82% — The much faster NDI input now handling many formats, sources, and up to 4K footage.
- 9:29→9:39 [AdvancedFeedback2] · explained · scripted · 90% — AdvancedFeedback2 with a wide preset range for fluid-like feedback effects.
- 9:39→10:08 [Dither] · explained · scripted · 88% — The Dither effect for a retro look, its minimal-parameter design philosophy, docs, presets, and bias-and-gain controls.
- 10:13→10:18 [ShardNoise][WorleyNoise] · explained · scripted · 65% — Two new noise variants, "Worley" and "Shard".
- 10:18→10:24 [FractalNoise] · passing · scripted · 70% — Updated and cleaned-up FractalNoise.
- 10:24→10:33 [Tint][RemapColor] · explained · scripted · 82% — Tint and its bigger sibling RemapColors as bias-and-gain-friendly color adjusters, with RemapColors now doing color cycling.
- 10:33→11:01 [ParticleSystem] · explained · scripted · 88% — Particle recap: ParticleSystem with emit points, a force, and points output as a minimal particle setup.
- 11:01→11:27 [ParticleSystem] · in-depth · scripted · 80% — Reworked particle lifetime: the -1 default auto-derives max lifetime, and particle age in the w attribute is now normalized 0–1.
- 11:27→11:40 [DrawMeshAtPoints2] · explained · scripted · 85% — Using the normalized age attribute with DrawMeshAtPoints2 to color and scale instance geometry over lifetime.
- 11:40→12:30 [TextureMapForce] · in-depth · scripted · 72% — New texture-based force that accelerates particles along a connected signed-normal map, built here from text rendered to a target and blurred.
- 12:30→12:46 [TextureMapForce] · explained · scripted · 70% — Its twist parameter (180° reverses the normal direction) and a confine-depth parameter keeping particles within camera distance.
- 12:46→13:06 [SnapToAnglesForce] · explained · scripted · 85% — SnapToAnglesForce nudging particle velocity toward defined angles to form grid-like structures.
- 13:06→13:16 [ui:OperatorSettings] · explained · scripted · 70% — UI improvements in 3.9, including renaming input parameters directly from the editor.
- 13:26→13:36 [ui:FocusMode] · explained · scripted · 85% — Easier foreground/background switching in focus mode by clicking the white edge.
- 13:36→13:42 [ui:Settings] · passing · scripted · 75% — Re-scanning devices from the settings window without restarting Tooll.
- 13:52→14:07 [ui:GradientEditor] · explained · scripted · 80% — Gradient editing now supports undo/redo and shows the final step when distributing gradient steps evenly.
- 14:07→14:42 [ui:Graph] · explained · scripted · 78% — Roadmap: project/resource reorganization toward Vulkan (Linux/Mac) and the upcoming magnetic graph system for snappier live patching.
- 14:42→15:05 [] · passing · scripted · 100% — Closing call for non-coding contributions (testing, docs, ideas, sharing) and Discord invite.
