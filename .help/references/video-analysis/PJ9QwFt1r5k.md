---
video: PJ9QwFt1r5k
type: update
date: 2025-09-14
title: TiXL v4.0 — release overview: graph & timeline UI, SDF/field & color ops, particles, animation, I/O
duration: 16:43
---

A scripted narrated walkthrough of the TiXL 4.0 release covering the new install/export flow, then a tour of UI improvements (graph editing, timeline, dope sheet, gradient/color editors) followed by new and updated operators across SDF/fields, points, particles, mesh/rendering, animation, I/O, and colors. Most segments are quick passing mentions in a feature montage; a handful are explained in a sentence or two. Many ops point viewers to the separate Ashborn tutorial and recent meetups for depth.

## Mentions
- 0:00→0:58 [] · explained · scripted · 90% — Where TiXL now installs and stores data: program files vs. Documents/Tixl vs. AppData-Roaming, and how to relocate the project folder in settings.
- 1:31→2:15 [] · passing · scripted · 80% — Build/compile fixes for Windows Defender false positives, smaller backups, and export-as-executable now handling NDI/Spout/webcam I/O.
- 2:15→2:42 [] · explained · scripted · 85% — How visual unit tests render image sequences from demos and compare against reference images for stability.
- 2:51→3:05 [ui:Graph] · explained · scripted · 88% — Why dragging ops no longer disconnects them when snapping out of a group, and how the graph stays intact.
- 3:05→3:32 [ui:Graph] · explained · scripted · 82% — Reordering and dragging ops out of vertical/horizontal stacks with temporary drag connections to skim outputs.
- 3:32→3:48 [ui:Graph] · passing · scripted · 78% — Dragging vertical connection lines and improved auto-layout when connections change.
- 3:48→4:05 [ui:Graph] · explained · scripted · 80% — Red blinking highlight before replacing a connection, and thumbnails shown on snapped (triangle) connections via the toolbar preview toggle.
- 4:05→4:15 [ui:Graph] · passing · scripted · 78% — Horizontal snapping on by default and aligning ops left with the new Alt+A shortcut.
- 4:15→4:30 [] · passing · scripted · 80% — Where to find the Keyboard Layout Editor for customizing shortcuts.
- 4:30→4:46 [ui:Graph][ui:SearchWindow] · passing · scripted · 75% — Smoother view animation when scrolling through Control-F search results, plus the reworked rename-annotations flow.
- 4:52→5:18 [ui:Timeline] · explained · scripted · 85% — Timeline editor changes: time clips auto-avoid overlap, resizable clip area, keyboard rename/delete, and fading of unconnected (never-evaluated) clips.
- 5:18→5:31 [ui:DopeSheet][ui:CurveEditor] · explained · scripted · 85% — Re-implemented curve editing in the dope sheet for more efficient handling of many keyframes.
- 5:31→5:47 [ui:ColorEditor] · explained · scripted · 82% — HDR colors above 1 now flagged with a triangle (including in gradients) for easier work with glow.
- 5:47→6:02 [ui:GradientEditor][BuildGradient] · explained · scripted · 85% — Gradient editor curve overlays on hover for understanding interpolation types; teaser for the [BuildGradient] op.
- 6:03→6:22 [] · explained · scripted · 80% — Choosing whether numeric parameters clamp on min, max, or both — useful for lower-bound-only values like particle count or blur radius.
- 6:23→6:43 [] · explained · scripted · 78% — Operators rewritten so custom UIs survive project rebuilds and support hot code reloading.
- 6:43→7:11 [ui:FocusMode][ui:ProjectPanel] · passing · scripted · 75% — Quality-of-life: pin composition with P/Ctrl+P in Focus Mode, background fade, scrollable/sorted project list, open project in Explorer.
- 7:16→7:28 [ui:Asset] · passing · scripted · 72% — Improved asset picker drop-downs as a stopgap before the 4.1 asset workflow.
- 7:28→7:35 [] · passing · scripted · 70% — Gradient parameters on ops can now be edited without restarting.
- 7:35→7:50 [ui:OutputWindow] · explained · scripted · 78% — Better grid visualization of float/int lists and adjustable column count for ArtNet light fixtures.
- 7:55→8:31 [ui:AudioInput][ui:Settings] · explained · scripted · 78% — Live-performance audio: working mute button, project volume override, rewritten soundtrack/background swapping, toggleable tempo locking, and a 600 BPM beat-tap ceiling.
- 8:31→8:54 [ui:ShaderGraph][HeightMapSdf] · explained · scripted · 80% — New shader-graph ops; using [HeightMapSdf] to build terrains (see the Ashborn tutorial).
- 8:54→9:14 [InvertSDF][TranslateUV] · explained · scripted · 75% — Inverting fields with the invert-field op and offsetting an SDF's local space with [TranslateUV].
- 9:33→9:54 [ui:ShaderGraph][VectorFieldForce][SdfToVector] · explained · scripted · 80% — Shader graph now outputs vector fields; drive particles with [VectorFieldForce] and generate fields via [SdfToVector] sampling the distance gradient.
- 9:54→10:01 [SDFToColor][BoxSDF] · passing · scripted · 75% — [SDFToColor] fixed for particle effects and [BoxSDF] implementation improved.
- 10:01→10:20 [BendField][PlaneSDF][TransformField] · explained · scripted · 72% — [BendField] fix, more intuitive gizmo translation of [PlaneSDF], and [TransformField] distance correction to avoid ray-march artifacts under scaling.
- 10:20→10:30 [CombineFieldColor] · passing · scripted · 72% — Rotate option for vector fields and a new mix-mode parameter on [CombineFieldColor].
- 10:30→10:49 [SelectPointsWithSDF] · explained · scripted · 85% — The new [SelectPointsWithSDF] op enabling a wide range of point effects (covered in a recent meetup).
- 10:49→11:04 [RandomizePoints][RadialPoints] · explained · scripted · 78% — [RandomizePoints] gains uniform scale, unclamped HDR colors and a color-randomize fix; [RadialPoints] gains a color parameter.
- 11:04→11:17 [MeshFacesPoints][SnapPointsToGrid][SetPointAttributes] · passing · scripted · 74% — Cleanup/examples for [MeshFacesPoints]; strength-FX parameter added to [SnapPointsToGrid] and [SetPointAttributes].
- 11:20→11:33 [PointTrail][PointTrailFast] · explained · scripted · 76% — New consistent default point-trail; the old gappy fast-cycle implementation renamed to [PointTrailFast].
- 11:36→12:09 [VelocityForce][ToroidalVortexField][RandomJumpForce][VectorFieldForce] · explained · scripted · 80% — New particle forces: [VelocityForce] for music sync, [ToroidalVortexField] for mushroom clouds, [RandomJumpForce] for velocity-preserving offsets, plus [VectorFieldForce].
- 12:09→12:27 [ParticleSystem][SelectPointsWithSDF] · explained · scripted · 76% — [ParticleSystem] gains an FX emit-velocity vector that pairs with [SelectPointsWithSDF].
- 12:27→12:35 [FieldVolumeForce][TurbulenceForce] · explained · scripted · 76% — [FieldVolumeForce] overhauled with colorization and optional collisions; [TurbulenceForce] now supports value fields.
- 12:35→12:52 [IcosahedronMesh] · passing · scripted · 75% — New [IcosahedronMesh] op with many parameters and examples (Nevemka).
- 12:52→13:03 [DrawLines] · explained · scripted · 78% — [DrawLines] FadeOutLongLines parameter for plexus-like effects.
- 13:03→13:08 [DrawMesh][RaymarchField][DrawPointsShaded][DrawMeshAtPoints][DrawPoints] · explained · scripted · 74% — Aligned PBR rendering across [DrawMesh], [RaymarchField], [DrawPointsShaded], [DrawMeshAtPoints], [DrawPoints], all now supporting color override via fields.
- 13:08→13:30 [SetEnvironment] · explained · scripted · 78% — [SetEnvironment] now auto-converts to a cube map, updates only when needed, and ships presets including a black one that disables environment light.
- 13:30→13:41 [RepeatAtPoints] · passing · scripted · 70% — Repeat-at-points now uses correct point scaling and FX scale factors.
- 13:41→13:55 [AnimBoolean] · explained · scripted · 80% — New [AnimBoolean] with a UI and CTRL-drag weight adjustment shared by all animate ops, plus speed factors.
- 13:55→14:01 [AnimInt] · explained · scripted · 78% — [AnimInt] gains a new UI and a modulo-loop parameter.
- 14:01→14:22 [SetKeyframes] · explained · scripted · 78% — New set-keyframe ops to generate keyframe tracks procedurally for consistent timeline animations (see Ashborn).
- 14:29→14:53 [VideoDeviceInput][ArtnetInput][NdiInput][SpoutInput][MidiInput] · explained · scripted · 76% — I/O updates: [VideoDeviceInput] webcam support, [ArtnetInput]/sACN integer values, and updated NDI/Spout/MIDI inputs.
- 14:53→15:14 [ColorsToList][CombineColorLists][PickColorFromList] · explained · scripted · 78% — New color-list type and its operators for building palettes, then combining or picking from the list.
- 15:14→15:27 [BuildGradient] · explained · scripted · 82% — [BuildGradient] turns a color list into a gradient with an optional float list for step positions.
- 15:56→16:10 [Sketch] · explained · scripted · 80% — The revamped [Sketch] operator for annotating animations and sketching storyboards (featured in the Ashborn tutorial).
- 16:10→16:43 [] · passing · scripted · 70% — Wrap-up: 4.1 roadmap for autumn 2025 and community channels.
