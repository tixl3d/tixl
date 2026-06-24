---
video: 5KX92VWEAG8
type: meetup
date: 2025-11-10
title: Beginners UI tour, real-time performance, and building a "donut" with SDFs, materials, lights, fog and sprinkles
duration: 2:28:27
---

A beginners walkthrough that opens with installation, the project panel, and where settings/projects live, then explains TiXL's CPU/GPU real-time model and the three classic performance bottlenecks (poly count, fill rate, memory). The bulk is a live build of a "donut" from a torus SDF, adding camera, materials, a faked floor and shadow, point lights, image-based lighting, fog, color grading with scopes, and finally scattering sprinkles with grid points pushed onto the surface. It closes with workflow advice on copying references side by side and reconstructing gradients and materials.

## Mentions
- 1:38→3:14 [ui:ProjectPanel] · passing — Where to find TiXL online: the website, GitHub, and the wiki that serves as user documentation.
- 4:10→9:15 [ui:Settings] · explained — How installation works and where TiXL keeps your settings (AppData\Roaming) versus your projects (Documents), plus the OneDrive caveat on Windows 11.
- 9:15→9:34 [ui:ProjectPanel] [ui:UserProject] · explained — How to create a new project from the project panel with the plus button.
- 12:47→13:13 [Sketch] · passing — Using the [Sketch] operator as an in-editor sketch-pad to annotate and explain ideas live.
- 13:13→15:55 [ui:EvaluationContext] · in-depth — The big picture of how TiXL evaluates every frame across CPU and GPU, and why that split makes it fast.
- 15:55→17:06 [ui:SymbolLibrary] [Raster] · explained — How the symbol library is organized and why an operator's output color (e.g. a texture) tells you its data type.
- 17:06→18:36 [GridPoints] [DrawPoints] · explained — The difference between texture, buffer (point), and draw-command operator colors, and why a buffer only shows its size.
- 21:22→23:08 [DrawPoints] [ui:PerformanceMonitor] [ui:InfinitySlider] · in-depth — Reading the performance graph and pushing point counts up with the infinity slider to see poly-count slowdown.
- 23:08→26:00 [DrawPoints] · explained — Fill-rate / overdraw: why large overlapping point sprites slow the GPU even at low counts.
- 27:00→31:00 [Camera] [RenderTarget] [ColorGrade] [Bloom] · in-depth — Rendering points through a camera into a fixed-resolution texture, and why TiXL copies a buffer for each effect instead of editing it in place.
- 31:00→32:46 [ui:OutputSettings] · explained — Resolution modes, 8K vs 4K vs Full HD pixel counts, and toggling V-sync to read true frame times.
- 38:42→41:11 [TorusMesh] [ui:SymbolBrowser] · explained — How fuzzy operator search works: typing "donut" finds [TorusMesh] via synonyms, and why "tm" surfaces the right ops.
- 41:11→43:11 [DrawMesh] [RaymarchField] · explained — Two ways to make a donut: drawing the [TorusMesh] vs. rematching it as an SDF field with [RaymarchField].
- 43:11→44:48 [ui:OutputWindow] · explained — How the output window's fill mode conforms to the aspect ratio and why you need a real camera.
- 44:48→48:00 [Camera] [ui:OutputWindow] · in-depth — Adding a [Camera] operator, pinning its output, and capturing a screenshot at a chosen resolution.
- 48:00→50:00 [ui:Graph] · in-depth — The magnetic graph: long-pressing to select, dragging to disconnect, and snapping ops into vertical stacks.
- 50:00→52:51 [ui:ShaderGraph] [NoiseDisplaceSDF] [ui:Field] · explained — Inserting a node mid-connection and displacing the SDF field with noise for an irregular donut surface.
- 52:51→55:00 [SetMaterial] · explained — Adding color and shininess with [SetMaterial] and bypassing it to compare with/without.
- 55:00→56:40 [ui:OperatorSettings] · explained — Renaming an operator instance and finding its real type in the top-left corner.
- 56:40→1:00:00 [SetMaterial] [Camera] · in-depth — Duplicating with Ctrl+D, multi-input insertion points, and why the last material/camera in a stack wins.
- 1:00:40→1:02:27 [Group] [Execute] · explained — The difference between [Group] (has a position) and [Execute] (faster, no position) for combining objects.
- 1:02:27→1:03:55 [ui:Graph] · explained — Understanding multi-inputs: how a node accepts as many connections as you want, rendering an object twice.
- 1:03:55→1:06:17 [QuadMesh] [DrawMesh] · in-depth — Adding a floor plane and learning about back-face culling and the "both-sidedness" parameter.
- 1:06:17→1:09:08 [ui:Gizmo] · in-depth — TiXL's right-handed Y-up coordinate system and how rotation gizmos map to X/Y/Z.
- 1:09:08→1:11:19 [TransformMesh] [Transform] [ui:Gizmo] · explained — Moving the floor with [TransformMesh] vs. [Transform], plus copy/paste of parameter values by name.
- 1:11:19→1:17:27 [PointLight] [SetEnvironment] · in-depth — Why a default light exists, adding [PointLight]s, and image-based lighting via [SetEnvironment] presets.
- 1:17:27→1:18:35 [ui:Gizmo] · passing — Hiding gizmos except when an operator is selected via the show-gizmos parameter.
- 1:18:35→1:21:00 [Blob] [DrawMesh] [SetShadow] · in-depth — Faking a floor shadow with a [Blob] texture and dealing with Z-fighting between coplanar surfaces.
- 1:21:00→1:24:00 [ui:ParameterWindow] · in-depth — The depth/Z-buffer explained, disabling the Z-test, and adjusting draw order, opacity, and luminosity with mouse drags.
- 1:24:00→1:27:33 [MakeResolution] [Blur] · explained — The "magic" 0,0 resolution that inherits the output size, and forcing a fixed resolution for a blurred overlay.
- 1:27:33→1:31:08 [Blob] [Bloom] · explained — Adding a negative [Blob] as a vignette and stacking a [Bloom] glow over the image.
- 1:31:08→1:34:06 [Bloom] [GradientEditor] · in-depth — Colorizing a glow gradient and shaping highlights/shadows with gain/bias and the mapping curve.
- 1:34:06→1:37:47 [ImageLevels] [ui:CurveEditor] · in-depth — Using an [ImageLevels] scope to read color intensity and avoid clipping bright values.
- 1:34:55→1:35:30 [ToneMapping] · passing — Mentioning [ToneMapping] as the smartest way to tame over-bright highlights.
- 1:37:47→1:40:45 [WaveForm] [ColorGrade] · in-depth — The [WaveForm] scope as a per-column histogram for spotting clamping and correcting color casts.
- 1:41:03→1:48:30 [SetEnvironment] [RoundedRect] [ColorEditor] · in-depth — Order-dependence of [SetEnvironment]: building an HDRI-like environment from a [RoundedRect], boosting its brightness with Ctrl, and why camera order matters for backgrounds.
- 1:48:30→1:51:56 [SetFog] · in-depth — Fading the scene to a color by camera distance with [SetFog], and adding mesh subdivisions to avoid shading artifacts.
- 1:51:56→1:55:21 [GridPoints] [DrawBillboards] [ui:Gizmo] · explained — Generating sprinkle positions with [GridPoints] in cell vs. bounds mode and moving them via the transform gizmo.
- 1:55:21→1:59:00 [CylinderMesh] [DrawMeshAtPoints] [ui:AssetLibrary] · in-depth — Previewing the upcoming asset library, picking a beveled cylinder, and drawing meshes at points with [DrawMeshAtPoints].
- 1:59:00→2:03:13 [TransformPoints] [MoveToSDF] · in-depth — Scaling points in point-space with [TransformPoints] and snapping sprinkles onto the donut surface with [MoveToSDF].
- 2:03:13→2:05:21 [RandomizePoints] [CylinderMesh] · in-depth — Randomizing sprinkle position and rotation, and why a low-poly cylinder costs nothing visually.
- 2:08:42→2:11:08 [ui:Annotations] [PickInt] · explained — Divergent vs. convergent workflow: grouping the "ugly donut" into an annotation and using a [PickInt] to iterate variations.
- 2:11:08→2:15:23 [LoadImage] [TorusMesh] [RadialPoints] · in-depth — Iterating on sprinkle shapes by swapping meshes and point generators ([RadialPoints]) to discover new forms.
- 2:15:23→2:21:07 [LoadImage] [CompareImages] · in-depth — A learning workflow: importing a reference image with [LoadImage] and reproducing it side-by-side with [CompareImages].
- 2:21:07→2:25:00 [LinearGradient] [QuadMesh] [NormalMap] [ColorGrade] · in-depth — Reconstructing a reference scene's gradient floor, normal-mapped bumps, color grade, and vignette step by step.
