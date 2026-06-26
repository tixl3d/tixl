---
video: PJ9QwFt1r5k
type: update
date: 2025-09-14
title: TiXL v4.0 — All New Features and Updates
duration: 16:46
---

The v4.0 release walkthrough: the new install/export flow and project-data locations, a pass of graph/timeline UI improvements, and a long tour of new and updated shader-graph, point, particle, mesh, animation, I/O, and color operators.

## Mentions
- 0:21→1:13 [ui:ProjectPanel] · explained · scripted · Concept · 70% — Install now writes to Program Files like a normal app, so project data lives under Documents (relocatable in settings) and settings/logs/backups under AppData-Roaming.
- 2:15→2:42 [ui:SkillQuest] · explained · scripted · Concept · 55% — New visual unit tests render image sequences for major projects and compare them to references, catching breaking changes before release.
- 2:57→3:32 [ui:Graph] · explained · scripted · Gotcha · 72% — Operators no longer auto-disconnect when dragged out of a group (it broke graphs too often); you can still reorder within vertical/horizontal stacks, with connections temporary during the drag.
- 3:32→4:00 [ui:Graph] · explained · scripted · Tip · 70% — You can finally drag vertical connection lines, and connections about to be replaced now blink red before you drop.
- 4:00→4:09 [ui:Graph] · passing · scripted · Tip · 65% — Thumbnails now show on every connection (even snapped-in ones), toggled by the toolbar preview button; horizontal snapping is on by default.
- 4:52→5:14 [ui:Timeline] · explained · scripted · Concept · 72% — Time clips auto-avoid overlap, the clip area is resizable, and clips connected to nothing fade out and say so in the tooltip since they're never evaluated.
- 5:20→5:31 [ui:CurveEditor] · explained · scripted · Performance · 70% — Curve editing in the dope sheet was re-implemented to be more efficient, so you can work with far more keyframes.
- 5:31→5:45 [ui:ColorEditor] · explained · scripted · Tip · 75% — HDR colors above 1 are now flagged with a triangle (also in gradients), making glow and HDR work much easier to read.
- 5:47→6:01 [ui:GradientEditor] · explained · scripted · Tip · 72% — Curve overlays now show on hover and while editing, making the different gradient interpolation types legible at a glance.
- 6:03→6:22 [ui:ParameterWindow] · explained · scripted · Parameters · 78% — Numeric parameters can now be clamped on min, max, or both — ideal for one-sided ranges like particle count or blur radius.
- 6:43→6:56 [ui:FocusMode] · passing · scripted · Tip · 62% — With nothing selected, press P (or Ctrl+P in Focus Mode) to pin the current composition.
- 8:47→9:00 [HeightMapSdf] · passing · scripted · Concept · 55% — A new field op that turns a height map into an SDF for building terrains.
- 9:00→9:14 [InvertSDF] · passing · scripted · Concept · 60% — Inverts a field's inside/outside, the quick way to turn a solid into a cavity.
- 9:33→9:49 [VectorFieldForce] · explained · scripted · Concept · 72% — The shader graph now carries vector fields, and this force steers particles along one (SDF-to-Vector samples the distance-field gradient to generate it).
- 9:54→10:01 [SDFToColor] · passing · scripted · Gotcha · 70% — Fixed so it works correctly when feeding particle effects.
- 10:07→10:16 [TransformField] · explained · scripted · Gotcha · 72% — Scaling a field now adjusts the returned distance too, avoiding ray-march artifacts that a raw scale introduced.
- 10:30→10:46 [SelectPointsWithSDF] · in-depth · scripted · Example · 80% — The headline point op: select points by their distance to a field to unlock a wide range of masked point effects (covered in depth in a recent meet-up).
- 10:53→11:08 [RandomizePoints] · explained · scripted · Parameters · 72% — Gains a uniform-scale parameter, stops clamping HDR colors, and fixes color randomization.
- 11:10→11:26 [MeshFacesPoints] · passing · scripted · Concept · 60% — Cleaned up with new examples; [RadialPoints] also gains a color parameter.
- 11:36→11:52 [VelocityForce] · explained · scripted · Example · 70% — New force that pushes particles forward along their velocity — handy for syncing bursts to music.
- 12:06→12:26 [FieldVolumeForce] · explained · scripted · Parameters · 72% — Overhauled to support colorization and optional collisions; [ParticleSystem] also gains an FX emit-velocity vector that pairs well with SDF point selection.
- 12:27→12:30 [TurbulenceForce] · passing · scripted · Concept · 65% — Now supports value fields as its noise source.
- 12:36→12:47 [IcosahedronMesh] · passing · scripted · Concept · 65% — A new icosahedron primitive with many parameters and examples.
- 12:47→12:57 [DrawLines] · explained · scripted · Tip · 72% — A fade-out-long-lines parameter cleanly suppresses stretched links, great for plexus-style effects.
- 12:57→13:07 [DrawMesh][RaymarchField][DrawPointsShaded][DrawMeshAtPoints] · explained · scripted · Concept · 70% — The PBR path was aligned across all the main draw ops, and they can now all override color through fields.
- 13:08→13:29 [SetEnvironment] · explained · scripted · Tip · 72% — Now auto-converts to a cube map and updates only when needed (no Live-Update toggle), with presets including a black one that fully disables environment light.
- 14:01→14:11 [SetKeyframes] · explained · scripted · Example · 70% — New operators that generate keyframe tracks procedurally, for consistent timeline-driven animation.
- 14:29→14:53 [VideoDeviceInput][MidiInput] · passing · scripted · Concept · 65% — Video device input now takes webcam input; Artnet/NDI/Spout/MIDI input ops were updated too.
- 15:14→15:27 [BuildGradient] · explained · scripted · Example · 75% — Turns a new color-list type into a gradient, with an optional float list to place the steps.
- 15:51→16:09 [Sketch] · explained · scripted · Tip · 70% — The revamped sketch op stays useful for annotating animations or blocking out storyboards.

UNSURE: HeightMapSdf, InvertField, SDFToVector, ToroidalVortexField, RandomJumpForce, PointTrailFast, SnapPointsToGrid (new v4.0 op names announced but not verified against the current vocab — bracketed at low confidence where I was fairly sure, else left in prose)
