---
video: a5_xCfC_3m4
type: meetup
date: 2026-02-03
title: Performance monitor & fill-rate testing, the new asset browser, MediaPipe hand-tracking experiments, and Skill Quest authoring
duration: 4:10:44
---

A ~4-hour TiXL community meet-up covering the new project/asset path restructuring and asset browser, a live walkthrough of the performance monitor and how to stress-test fill-rate and draw-calls, and a long live experiment building a MediaPipe hand-tracking particle toy. The second half demonstrates how Skill Quest tutorials and tour-points are authored, ending with member show-and-tell of an Assimp importer, an AI HLSL shader generator, and a VAT mesh-animation setup.

## Mentions
- 0:09→0:12 [ui:Settings] · passing · answer · 70% — Where to change the editor font size in the settings if the UI is too small.
- 20:45→23:25 [ui:PerformanceMonitor] · in-depth · discussion · 90% — How to read the performance graph: frame duration (ms), UI update time, garbage collection, draw calls and shader dispatches — and what counts as healthy.
- 23:40→25:15 [Blob] [FractalNoise] [ui:SymbolBrowser] · explained · experiment · 80% — How to add an operator from the background (right-click Add, or Tab) and why [FractalNoise] makes a good performance stress-test.
- 25:50→27:00 [FractalNoise] [Time] · explained · experiment · 85% — Why a cached image shows darker and how connecting a [Time] to a parameter forces continuous re-evaluation for performance testing.
- 27:00→30:40 [FractalNoise] · in-depth · experiment · 85% — How increasing resolution to 4K demonstrates fill-rate limits, and how toggling Vsync off gives a cleaner ms measurement.
- 30:40→33:20 [Text] [Loop] [GetFloatVar] · in-depth · experiment · 80% — How to stress draw-call count by looping [Text] many times and driving its position with a loop progress variable.
- 34:20→38:10 [ui:ProjectSettings] [ui:Asset] · explained · discussion · 80% — How projects now live in the Documents folder, why asset paths must stay relative, and the pain of the old resource-resolution guesswork.
- 38:10→40:05 [LoadImage] [ui:Asset] · explained · discussion · 80% — The new fixed asset-path format (project name + asset folder + filename, forward slashes) and why it's optimized for Linux.
- 40:05→43:00 [LoadImage] [PlayVideo] [PlayAudioClip] [ui:AssetLibrary] · in-depth · discussion · 85% — How the new asset browser lists assets across all projects, filters by type, and lets you drag assets straight onto the graph.
- 43:00→46:30 [PlayAudioClip] [ui:AssetLibrary] · explained · experiment · 75% — How dragging external files (audio, images) into the asset browser imports and copies them into the project's asset folder.
- 46:30→48:30 [ui:Asset] · explained · discussion · 70% — How the internal asset database enables future rename-with-reference-updates and per-project dependency checks.
- 48:30→55:15 [LoadImage] [ui:Asset] · in-depth · discussion · 75% — How asset references work across projects (the "dot" indicators) and why keeping Lib free of external resources matters for sharing packages.
- 55:15→59:15 [ColorGrade] [ui:Asset] · explained · discussion · 70% — The plan for hashed thumbnails of assets and operator output previews, and why disabling live previews can speed up working.
- 1:00:00→1:02:30 [Text] [ui:Asset] [ui:ParameterWindow] · explained · discussion · 80% — Anatomy of a valid asset path (project name left, local asset-folder path right) and that pasting absolute paths is allowed but stays absolute.
- 1:02:30→1:07:20 [ui:Asset] · explained · discussion · 65% — How the old `t3` resource folder was cleaned up into Lib / examples / test-footage, and where the splash screen and shaders now live.
- 1:07:20→1:13:00 [LoadObj] [LoadGltfScene] [Transform] [ui:Asset] · explained · discussion · 70% — A user's idea to drag a 3D object straight into a 3D scene and place it, and how asset types could drive smart drop behavior.
- 1:13:00→1:16:00 [HandLandmarkDetection] [VideoDeviceInput] · explained · experiment · 75% — First look at MediaPipe hand-landmark detection from a webcam, and the snag that OBS/Discord may already hold the camera.
- 1:18:50→1:19:40 [ui:SearchWindow] · passing · answer · 80% — Hidden tip: Ctrl-F first shows recently visited operators as a history, and Alt-Left/Right navigates selection history.
- 1:24:00→1:28:30 [HandLandmarkDetection] [TransformImage] [ConvertFormat] · in-depth · experiment · 80% — Why the camera feed needs BGRA/BGR8 format handling and how [ConvertFormat] fixes a flipped/broken hand-detection image.
- 1:28:30→1:30:00 [DrawLines] [DrawRayLines] [HandLandmarkDetection] · explained · experiment · 70% — Visualizing the 21 hand landmarks and the difference between [DrawLines] and [DrawRayLines].
- 1:30:00→1:31:30 [FilterPoints] [DrawPoints] [HandLandmarkDetection] · explained · experiment · 75% — How to isolate the thumb (index 4) and index-finger tip (index 8) with [FilterPoints] and draw them.
- 1:31:30→1:33:20 [BlendPoints] [DrawPoints] · explained · experiment · 75% — How to find the midpoint between two fingertips by blending points entirely on the GPU.
- 1:33:20→1:35:00 [PointSimulation] · explained · experiment · 70% — Trying [PointSimulation] (a.k.a. "damp points") to smooth jittery tracking, and why it scrambles point indices.
- 1:35:00→1:37:30 [HandLandmarkDetection] [ImageSegmentation] · in-depth · discussion · 80% — Technical background on MediaPipe: local neural-network assets, permissive license, and how it loads under the new asset structure.
- 1:37:30→1:42:00 [PointsToCPU] [GetPointDataFromList] [Vec3Distance] · in-depth · experiment · 75% — Reading points back to the CPU with [PointsToCPU], then [GetPointDataFromList] to extract two positions and [Vec3Distance] for finger distance.
- 1:40:40→1:41:40 [GetPointDataFromList] [ui:ParameterWindow] · passing · discussion · 65% — A usability gripe: operator-picker only suggests for the primary input, hiding the data-list input you actually need.
- 1:42:00→1:45:00 [Remap] [ParticleSystem] [TurbulenceForce] · explained · experiment · 75% — Using [Remap] on the finger distance to drive a [ParticleSystem] emit velocity with [TurbulenceForce].
- 1:45:00→1:50:00 [Atan2] [RotateAxis] [Vector2Components] · in-depth · experiment · 65% — Wrestling with [Atan2] and vector components to compute an emit rotation angle, and the confusion of rotating on the wrong axis.
- 1:50:00→1:54:00 [TransformPoints] [RotateAxis] · in-depth · experiment · 65% — Breaking the rotation problem into steps and the coordinate-system trouble when aiming the particle emitter along the finger line.
- 1:54:00→1:59:30 [TurbulenceForce] [DirectionalForce] [Remap] · in-depth · experiment · 75% — Adding [DirectionalForce] gravity and remapping velocity so the particle stream finally responds to hand movement.
- 1:59:30→2:01:30 [ImageSegmentation] · explained · experiment · 75% — Trying the MediaPipe selfie segmenter mask and why the multi-class model is noticeably slower.
- 2:03:00→2:05:30 [FastBlur] [ImageLevels] [TemporalAccumulation] · in-depth · discussion · 85% — Why [FastBlur] (Kawase-style up/down pyramid) is ~10x faster for large kernels, and using [TemporalAccumulation] to damp jittery segmentation.
- 2:05:30→2:09:00 [NormalMap] [TextureMapForce] [ParticleSystem] · in-depth · experiment · 75% — Turning the blurred mask into a signed [NormalMap] and feeding [TextureMapForce] (screen-space) to push particles around the silhouette.
- 2:09:30→2:16:00 [SelectPoints] [HandLandmarkDetection] · in-depth · discussion · 70% — The two-hands index-instability problem and ideas for a "pick points from buffer" op or a structured world-landmarks buffer with handedness.
- 2:19:00→2:21:00 [ui:Graph] · explained · answer · 80% — Cleanup tricks: "select connected" and "select inputs" to find and delete operators that contribute nothing.
- 2:22:00→2:32:00 [GpuMeasure] [DrawPoints] [ParticleSystem] [TextureMapForce] · in-depth · answer · 80% — A rough performance budget breakdown of the whole MediaPipe graph (drawing points is the costly part) and why the webcam/segmentation runs multithreaded without dropping the main framerate.
- 2:32:00→2:33:00 [VideoDeviceInput] [TransformImage] · passing · answer · 75% — Tip that [VideoDeviceInput] has a built-in flip-vertically, so you don't need a separate [TransformImage].
- 2:33:20→2:38:20 [ui:SkillQuest] [ui:SkillMap] [ui:ProjectPanel] · explained · discussion · 75% — Intro to the new Skill Quest panel on the home canvas: how to enable/disable it and the long-term vision of branching tutorials.
- 2:38:20→2:50:30 [ui:SkillMap] [ui:SkillQuest] · in-depth · discussion · 75% — Walkthrough of the Skill Map editor and Tour-Point editor: defining requirements/links and exporting tour-point text as Markdown.
- 2:50:30→2:55:00 [ui:SkillQuestLevel] · explained · discussion · 70% — Workflow tip: write tutorial text first in Markdown, then build the graph steps to match.
- 3:04:00→3:13:30 [ui:SkillQuestLevel] [ui:ParameterWindow] · in-depth · experiment · 70% — Live-authoring a "what is a point" lesson and the discovery that a point has more than just a position (color, size, rotation).
- 3:13:30→3:26:00 [DrawMeshAtPoints] [PerlinNoise3] [SampleGradient] [DrawPoints] · in-depth · experiment · 75% — Building the point-attributes puzzle: connecting [PerlinNoise3] to position, [SampleGradient] to color, noise to scale, and why starting with position reads best.
- 3:26:00→3:38:00 [ui:SkillMap] [ui:SkillQuestLevel] · in-depth · experiment · 70% — Wiring the new lesson into the skill map (namespace, "any input path" unlock link) and the "cheat" where multiplied colors let a wrong answer still pass.
- 3:42:00→3:47:30 [RadialGradient] [LinearGradient] · explained · discussion · 75% — The gradients tutorial example (make a circle by adjusting [RadialGradient] width/offset) and a plan to use parameter snapshots as graded solutions.
- 3:51:30→3:54:00 [OnvifCamera] [LoadObj] [ActionCamera] · explained · discussion · 70% — Member show-and-tell: an ONVIF-controlled PTZ camera for AR, and an Assimp-based importer node loading/exporting many 3D + point-cloud formats.
- 3:54:00→3:59:30 [CustomPixelShader] [CustomPointShader] [CustomSDF] · explained · discussion · 75% — Demo of an AI HLSL shader generator wired to [CustomPixelShader]/[CustomPointShader], with an undo history of generated scripts.
- 3:59:30→4:05:00 [DisplaceMeshVAT] [RepeatMeshAtPoints] [GridPoints] [TranslateUV] · in-depth · discussion · 80% — VAT vertex-animation demo: using [DisplaceMeshVAT] and randomizing the F2 texture-coordinate per copy so repeated meshes dance out of sync.
- 4:05:00→4:09:30 [RepeatMeshAtPoints] [RadialGradient] [AttributesFromImageChannels] [LoadGltfScene] · explained · discussion · 70% — Why per-copy vertex deformation breaks under rotation in [RepeatMeshAtPoints], and discussion of GLTF skeletal animation as the next building block.
