---
video: GF0wWpNWkVs
type: meetup
date: 2026-02-09
title: Part 2 — Building a ProjectLight post-effect operator (projector/shadow ray-marching, camera reference, PickObject, custom shader extraction)
duration: 3:14:54
---

In Part 2 of a TiXL community meet-up, the host wraps a complex projected-light / volumetric ray-marching effect into a reusable ProjectLight post-effect operator, wiring up scene/image inputs, a cached camera reference, and a new PickObject operator to switch between projector camera types. He then extracts the inlined CustomPixelShader into a proper shader file, sets up constant-buffer parameters (intensities, colors, shadow bias/scale, ray decay, step count, time), and groups them under Light / Surface / Shadow headings. The session closes with debugging a one-frame camera-update lag, building an aspect-ratio-driven shadow resolution, adding a Bloom-lit example scene, and generating operator thumbnails.

## Mentions
- 0:34 [CustomPixelShader] · passing — the original inlined-shader "guy" being converted into a reusable post-effect operator
- 1:23 [RenderPostEffects] · passing — debating whether the new op should be classified as a post-effect under the Lib/render/post-effects namespace
- 9:23 [GetFirstValidTexture] · explained — "first valid texture" fallback so the scene image is used when present, otherwise a blob/default
- 10:40 [CameraReference] · explained — adds a required camera reference OBJ input ("view cam reference") feeding the effect
- 11:49 [GetMainCamera] · passing — the default "main camera" source being pinned so it doesn't keep switching
- 12:42 [ReuseCamera] · explained — reusing the outer/main camera inside the sub-graph; "this is the view, this was the main, here we use reuse camera"
- 13:29 [SetCamera] · explained — discovers the camera-definition node is a Set, needed so the camera definition actually updates downstream
- 16:30 [Time] · explained — discussing per-frame caching so both branches update once; touches local reference time and motion-blur concerns
- 18:49 [HoldFloat] · passing — sketching a one-frame hold/delay on the camera value as a possible workaround
- 21:18 [OrbitCamera] · explained — switches the main camera to an orbit camera to rotate the projector view
- 22:48 [OffsetValue] · passing — mentions adding an offset/animation control to the camera motion
- 23:15 [LoadImage] · explained — plugging the existing image (vs. an OBJ) into the projector input; later loads a frog image
- 25:03 [Perspective] · explained — current projector uses a perspective projection; later weighed against orthographic
- 26:24 [PickObject] · in-depth — creates a new "pick" operator via Duplicate-as-new-type to switch between connected objects (camera/projector types); long discussion of the generic-object cast hack, renaming, and unique-id change in code
- 30:29 [Time] · in-depth — deep dive into Time's modes: local idle-motion time, local time, set-time remapping, playback time, and app-start time, and when each is useful
- 31:41 [SetTime] · explained — remapping local time via SetTime to drive the graph
- 31:55 [FloatToString] · passing — float-to-string text used to visualize/evaluate the local time value
- 32:26 [Modulo] · passing — modulo/loop on time so the value keeps running and wraps
- 33:34 [TimeConstBuffer] · in-depth — critiques the legacy time constant-buffer as an early hack that may break motion-blur multi-pass rendering; argues for using the elaborate Time path instead
- 34:28 [RenderWithMotionBlur] · explained — example of rendering a texture then applying motion blur, motivating frame-back-time correctness
- 36:32 [PickTexture] · passing — recalls intending PickTexture as the duplicate template (used PickObject instead)
- 37:18 [PickObject] · explained — wiring the finished PickObject: connections, index, multi-input; just assigns the selected object
- 38:32 [IntValue] · explained — int value acting as the camera/projector type selector feeding PickObject's index
- 41:08 [Remap] · passing — suggests a Remap for scaling rather than hard-coding scale
- 41:56 [PositionAndTarget] · explained — adds position and target inputs (vs. direction) for the projector
- 42:40 [QualityCenter] · passing — half-heard mention while adding a quality/step-count parameter
- 43:00 [StepCount] · explained — adds an integer step-count parameter for the ray march
- 45:30 [SetEnvironment] · explained — debates the SetEnvironment node; decides to use it with a black environment for the projector pass
- 49:36 [GetTextureSize] · in-depth — "texture properties / get texture size" to derive aspect ratio for the orthographic projection
- 50:51 [GetRequestedResolution] · explained — alternative "requested resolution" source; later tied to SetRequestResolution discussion
- 51:14 [Divide] · explained — divides texture width/height (a "vec2" split) to compute the aspect ratio
- 52:43 [OrthographicProjection] · explained — feeds aspect ratio into the orthographic projection mode
- 52:57 [CheckerPattern] · passing — "real chip pattern"/checker texture used to verify resolution adjustment
- 56:33 [Multiply] · explained — plan for two artistic colors: one multiplied onto the projected image, one for the scene color
- 57:55 [ProjectLight] · in-depth — opens the ProjectLight operator code; defines the private ProjectorTypes enum (orthographic, spotlight, directional)
- 1:05:08 [CustomPixelShader] · in-depth — long examination of the inlined CustomPixelShader: float buffer for colors, padding, vector2/vector4 layout, output params; argues it should become a real shader file
- 1:09:08 [Quaternion] · explained — tangent on how Quaternion vs Color differ only by UI; quaternions shown as a color picker
- 1:13:58 [GodRays] · explained — looks for a good example op (god-rays / RDS) to copy parameter wiring from
- 1:16:09 [LoadShader] · in-depth — creates a new default shader file, saves it under Lib assets/shaders/post-fx as ProjectLight.hlsl (PascalCase convention)
- 1:18:21 [RenderTarget] · explained — routes the shader output into render targets, rewiring the whole effect
- 1:19:59 [FloatsConstBuffer] · in-depth — reuses an existing floats constant buffer for the four packed parameters
- 1:20:45 [SamplerState] · explained — two samplers and two transform-cameras feeding the shader; later notices sampler state wasn't connected and plugs it in
- 1:20:53 [TransformCamera] · explained — view camera and projector camera transforms fed as constant buffers
- 1:22:13 [Vector4] · explained — vector4 params for ambient/scene color and light color
- 1:23:35 [FloatsToBuffer] · in-depth — assembling surface intensity, ray intensity, shadow bias, shadow scale, ray decay into the float buffer with correct slot order and renaming a→surface intensity, etc.
- 1:24:12 [RaysDecay] · explained — adds a "rays decay" float parameter controlling ray falloff
- 1:32:00 [Time] · in-depth — strongly argues for using Time (not CountInt) so frames are reproducible for video render and unit tests
- 1:33:43 [CountInt] · explained — proposed but rejected as time source because it breaks render reproducibility
- 1:35:08 [StepCount] · in-depth — feeds step count into shader; discusses shader-compiler loop-unrolling, static vs dynamic, and clamping step count to avoid GPU freeze
- 1:39:33 [ValuesToBuffer] · explained — float padding to align vector4s on constant-buffer rows
- 1:40:40 [AmbientColor] · in-depth — debugging why the ambient color isn't multiplied correctly; finds it's an in-out and multiplies it with the color parameter
- 1:44:08 [SetPixelShaderStage] · in-depth — sets the pixel-shader stage and its three constant buffers (params, resolution, view+projector cameras); checks buffer order
- 1:50:11 [OrthographicCamera] · explained — switches projection to orthographic and adjusts scale
- 1:51:24 [CheckerPattern] · passing — toggles back to the checker pattern picture vs. frog image to test
- 1:54:46 [WaveMarching] · in-depth — compares the old test vs new ray-marching path; notes it's slower because it may ray-march twice (shadow + main)
- 2:05:42 [ShadowResolution] · explained — adds an int "shadow resolution" parameter for the shadow pass
- 2:07:53 [FloatToInt] · explained — converts shadow resolution by aspect ratio then back to int to build a resolution
- 2:08:29 [IntToInt2] · explained — combines x and y ints into an int2 resolution for the shadow pass
- 2:11:30 [Bloom] · passing — "set fork"/glow shown working on the post-effect; later replaced with a proper Bloom
- 2:16:15 [SetRequestResolution] · in-depth — explains SetRequestResolution writes requested resolution into the eval context; a 0,0 texture then uses it, and the factor enables oversampling
- 2:17:16 [GetRequestedResolution] · explained — paired with SetRequestResolution; reads the requested resolution from context
- 2:27:16 [OrbitCamera] · explained — inspects OrbitCamera to see if it avoids the double camera-update problem (it doesn't)
- 2:30:00 [SmoothStep] · explained — adds a soft-edge / fade-out at the shadow-map sampling boundary ("soft edge")
- 2:33:22 [Button] · passing — "make a button" / PV trigger mentioned while experimenting
- 2:42:54 [ProjectorType] · explained — groups projector-type and camera reference params; sets up parameter groups
- 2:44:32 [ShowParametersWithGroup] · in-depth — organizes inputs into Light / Light Appearance / Surface / Shadow groups via "show parameter with group"
- 2:50:14 [WaveMarching] · explained — removes the slow ray-marching test path from the example, noting it's very slow
- 2:51:37 [Text] · explained — building a promo/example: Text → RenderTarget for the projected image
- 2:51:48 [RenderTarget] · explained — render target in the example promo chain
- 2:51:51 [SubdivisionStretch] · passing — "subdivision stretch or so" applied in the example scene
- 2:56:31 [Bloom] · in-depth — adds Bloom to the example; "with this new nice kernel we can physically control the falloff... looks much better with the bloom"
- 2:57:59 [Include] · explained — needs an include line in the shader; pastes the GPU-hash include and confirms it compiles
- 3:00:18 [ToneMapping] · explained — building the sample scene without tone mapping for now, wiring the example inputs
- 3:00:34 [Noise] · explained — notes the noise texture isn't strictly needed in the example but doesn't hurt
- 3:02:22 [Combine] · explained — wraps the sample-scene content into a Combine super-definition under the Examples namespace
- 3:04:27 [SetThumbnail] · in-depth — right-click "set thumbnail" on the symbol definition and example to generate operator thumbnails
- 3:08:33 [MotionBlur] · passing — checks MotionBlur examples while debugging why thumbnails/descriptions aren't showing
