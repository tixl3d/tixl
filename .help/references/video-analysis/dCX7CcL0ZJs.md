---
video: dCX7CcL0ZJs
type: meetup
date: 2026-04-21
title: Meet-up — Demo Timing, Sine Waves from Line Points, the Custom Point Shader & Rebuilding a Helix
duration: 3:41:08
---

A ~3.7h Q&A meet-up covering deterministic demo construction (time clips, preloading via set-time, smooth procedural timing), several ways to turn a line of points into a sine wave, the custom point shader and its SDF field input, point alpha/Z-sorting trade-offs, and a long live rebuild of a twisted-helix reference scene.

## Mentions
- 0:06:00→0:07:30 [PickTexture][Switch] · explained · discussion · Comparison · 82% — Both index between inputs, but [Switch] adds command/layer branching: index -1 disables all, -2 enables all, and positives wrap around — modes [PickTexture] lacks.
- 0:07:42→0:09:50 [ui:Timeline] · explained · discussion · Tip · 78% — Insert a clip on a layer to both label a time range and gate visibility: outside the clip its layer is skipped, inside it renders — a cleaner on/off than animating a switch index.
- 0:08:35→0:13:00 [TimeClip] · explained · discussion · Example · 85% — Drop a clip onto a layer/branch to enable it only within its span; name clips (press Return) and duplicate them to lay out demo sections without keyframing a selector.
- 0:11:30→0:15:00 [ParticleSystem] · explained · experiment · Gotcha · 75% — To make an emitter deterministic for a demo, drive its reset trigger from a keyframed boolean so jumping home re-seeds it; run-but-hide it early by filtering to a single point, then reveal by raising the count.
- 0:16:00→0:18:30 [ui:PlayerExporter] · explained · answer · Performance · 80% — The exporter now scans ahead and pre-compiles upcoming shaders (with shader-source caching so concatenated custom HLSL still hits the cache), removing the playback stutter older preloading missed.
- 0:18:30→0:24:00 [SetTime] · in-depth · answer · Example · 88% — Build a manual preloading screen: wrap the whole demo in a branch, animate a sweep 0→100 so every shader/clip is touched and pre-compiled behind a cover layer, then cut to live time. (As of 4.1 the player does this automatically.)
- 0:25:00→0:27:00 [LastFrameDuration] · passing · answer · Example · 65% — Combine with [RunTime] and a value-keeper to read a rough per-frame or precompute time as a debug readout.
- 0:29:30→0:33:30 [ui:EvaluationContext] · in-depth · answer · Gotcha · 80% — Parameter-input smoothing (default ~6 frames) softens every knob change, so changing a time-evaluated animation's rate "jumps" to where it should be — smoothing can't fix it because the value isn't accumulated.
- 0:30:00→0:31:30 [OrbitCamera] · explained · experiment · Tip · 72% — Connect its override-time input to bypass the internal spin rate and feed an external time/animation source to drive rotation precisely.
- 0:33:30→0:35:30 [Accumulator] · in-depth · answer · Concept · 85% — The escape hatch from time-evaluated animation to frame-dependent timing: it integrates an increment each frame, so a live knob (e.g. mouse X) ramps speed smoothly instead of snapping.
- 0:34:00→0:36:30 [Accumulator] · explained · answer · Gotcha · 80% — Left running for hours its value drifts into float-precision artifacts; periodically fire its reset, or keep increments small, to stay precise.
- 0:49:00→0:56:00 [LinearSamplePointAttributes] · in-depth · experiment · Example · 85% — The artist-friendly sine wave: sample a [LinearGradient] by each point's normalized index and map brightness to Y offset; set interpolation to smooth and animate the sample offset for motion.
- 0:51:30→0:56:00 [LinearGradient] · explained · experiment · Tip · 75% — Use it as an editable displacement curve — pick a preset, switch interpolation to smooth for a clean wave, and animate its offset since it's just a texture.
- 0:58:00→1:00:00 [CustomPointShader] · in-depth · experiment · Parameters · 90% — Its built-ins for developers: F is the normalized 0–1 point index; sample_gradient(t) recolors by a value; bias() remaps 0–1 with a gain/bias control; FX1/FX2 are two writable attributes downstream ops can read.
- 1:00:00→1:04:00 [CustomPointShader] · in-depth · experiment · Example · 82% — Write a height-derived value into FX1/FX2, then drive later modifiers (scale, [AddNoise] strength) by that attribute — e.g. no noise at the base, lots at the crest.
- 1:11:00→1:13:00 [BoxSDF] · explained · experiment · Concept · 72% — A distance function over space you can feed a point shader's field slot; shape it first with [RepeatAxis] and rounding before sampling per point.
- 1:13:00→1:15:00 [CustomPointShader] · in-depth · experiment · Example · 80% — Its field input accepts an SDF chain ([BoxSDF]/[SphereSDF]): call get_distance(p.position) inside the shader to read signed distance per point and deform/colorize by the field; combine with [TransformField] for moving effects.
- 1:18:30→1:21:30 [ImageLevels] · explained · experiment · Tip · 80% — Slide its slice to read an image's actual brightness distribution and discover a "white" sprite is really ~75–85% gray, or that a gray image carries a slight tint.
- 1:24:00→1:28:00 [ui:Field] · in-depth · discussion · Concept · 78% — Why soft point sprites fight the Z-buffer: a filtered edge has partial opacity, so an alpha-cutoff threshold decides which fragments write depth — too low gives noisy edges, too high makes circles hard.
- 1:28:00→1:29:00 [DrawPointsShaded] · explained · discussion · Parameters · 80% — Its alpha-cutoff sets the opacity above which a fragment writes depth, and its depth-write toggle trades correct sorting for soft round sprites — with depth off, draw order alone decides overlap.
- 1:29:00→1:32:00 [SortPoints] · in-depth · experiment · Gotcha · 82% — Sorts a point buffer back-to-front for correct alpha without depth-write, but measures distance to the camera origin (radial), so large camera-facing billboards can still sort wrong; reported bug when the count is a power of two.
- 1:30:00→1:31:30 [ReuseCamera] · explained · experiment · Gotcha · 70% — When feeding [SortPoints] a camera, note it caches and must be re-evaluated (plugged into the draw path or set to auto/look-at) before the sort sees the correct camera position.
- 1:40:30→1:42:00 [WaveForm] · explained · experiment · Tip · 75% — Its vectorscope mode reveals an image's color distribution at a glance — e.g. that most colors cluster in desaturated orange with a few solid highlights.
- 1:44:00→1:46:30 [RepeatMeshAtPoints] · explained · experiment · Example · 82% — Stack a [CubeMesh] along [LinePoints] and use its built-in twist (orientation axis Y) plus stretch to spiral the stack — staying one ~1200-face mesh rather than many draw calls.
- 1:46:30→1:47:30 [DisplaceMeshNoise] · explained · experiment · Tip · 70% — Raise (don't lower) its frequency to break a regular repeated mesh into organic distortion; keep the amount modest so structure stays readable.
- 1:23:00→1:23:30 [ToneMapping] · passing · experiment · Tip · 60% — Place it after [Bloom] so highlights roll off correctly once the glow is added.
- 2:09:00→2:15:00 [DrawMesh] · in-depth · discussion · Concept · 72% — Why a generic wireframe-thicken op is hard on the GPU: meshes are triangles and per-triangle compute can't see neighbors, so deduplicating shared edges is the blocker — cache one complex shape and repeat it instead.
- 2:36:30→2:39:00 [SSAO] · in-depth · experiment · Gotcha · 85% — Banding/hard occlusion edges come from poor depth-buffer precision under a narrow field of view; push the camera's near clip plane up to just in front of the object (and match the SSAO range) to restore precision.
- 2:37:00→2:40:00 [OrthographicCamera] · explained · experiment · Gotcha · 72% — Effects like [SSAO] can misbehave with it; faking ortho via a perspective camera pulled far back with a tiny field of view restores the depth effect.
- 2:53:00→2:56:00 [SSAO] · explained · experiment · Parameters · 78% — Its depth-range parameter ignores geometry beyond a set distance, letting you keep occlusion on a foreground shape while excluding a background plane.
- 3:01:00→3:06:00 [TileableNoise] · explained · experiment · Performance · 80% — Faster and better-looking than fading a non-tileable noise; for displacement, read it from a 32-bit single-channel (R) render format — higher precision and faster than 16-bit RGBA.
- 3:07:30→3:13:00 [ui:Graph] · explained · discussion · Tip · 75% — Cleanup workflow: "select connected" to isolate a branch, group into a named [Group] or lighter [Execute] (used purely for labeling), or combine into a new symbol while keeping the output connection.
- 3:14:00→3:20:00 [ui:OperatorSettings] · in-depth · experiment · Tip · 82% — Expose inner parameters as inputs on a combined symbol (Ctrl-break a param, set a default), then save named presets — TiXL auto-generates thumbnails and you can Alt-drag to blend continuously between them.
- 3:22:00→3:24:00 [CustomPointShader] · explained · experiment · Gotcha · 76% — Recolor points by deriving a value from p.position length into sample_gradient — but a metal material can override the written point color, so the tint may not show.
- 3:30:00→3:33:00 [SetEnvironment] · explained · experiment · Tip · 75% — Beyond an HDRI, plug a [TileableNoise] or any texture into it to light a scene with a synthetic environment, optionally with no visible background.
- 3:30:00→3:38:00 [ProjectLight] · in-depth · experiment · Example · 80% — For volumetric/directional shadows: feed it a point light and a separate camera reference (disconnecting the point light's own camera helps the shadows), then choose a projected image.

UNSURE: none
