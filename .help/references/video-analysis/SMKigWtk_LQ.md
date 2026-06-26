---
video: SMKigWtk_LQ
type: tutorial
title: HandsOn 001 — Build an Abstract 3D Animation in ~12 Minutes
duration: 0:14:51
---

A fast, scripted hands-on build of an abstract glowing 3D scene: a torus-studded sphere with scattered point clouds, two colored point lights, an orbiting camera, and a full post-processing stack (depth of field, glow, color grade, grain, chromatic distortion).

## Mentions
- 0:13→2:24 [SphereMesh] [DrawMesh] · explained · scripted · Example · 88% — Pair a primitive mesh with a draw operator to make geometry visible in the output; shrink the mesh radius before composing it with other shapes.
- 2:24→2:54 [TorusMesh] · explained · scripted · Parameters · 85% — Thin the ring by dialing its thickness/inner-radius down (here to ~0.03–0.15) so it reads as a slender band rather than a fat donut.
- 2:54→3:09 [Group] · explained · scripted · Example · 80% — Combine several geometry branches into one node so you can judge how the shapes relate in scale and overlap.
- 3:09→3:46 [GridPoints] · explained · scripted · Parameters · 85% — Drop the per-axis count (e.g. 3×3) and tighten the scale to get a sparse, compact point set to scatter copies onto.
- 3:46→4:35 [RepeatMeshAtPoints] · in-depth · scripted · Example · 90% — Wire a points set into one input and a mesh into the other to stamp a copy of the mesh at every point; chain it after the points generator that defines the layout.
- 4:35→5:00 [RandomizePoints] · in-depth · scripted · Parameters · 88% — Insert it behind a points generator and raise the W channel to vary per-instance scale, plus push rotation to ~360 on each axis so stamped copies face random directions.
- 5:26→6:07 [SetMaterial] · explained · scripted · Parameters · 80% — Drive emissive color plus a high luminance (e.g. 10) for a self-lit glowing surface, or lower roughness and raise metalness for a reflective metal look.
- 6:07→7:09 [SpherePoints] · explained · scripted · Parameters · 85% — Raise the point count and shrink the radius to wrap a dense shell of points around an object; follow with a randomizer to break the too-perfect spherical surface.
- 7:09→7:42 [DrawPoints] · explained · scripted · Example · 82% — Render a point set as visible dots and add it into the scene group; reuse the same generator with a wider spread for a separate background layer.
- 7:42→8:03 [RandomizePoints] · explained · scripted · Tip · 80% — Spread a cloned point cloud across the whole scene by setting all three position-randomize axes to the same value (e.g. 5) for a uniform scatter.
- 8:03→8:54 [PointLight] · in-depth · scripted · Parameters · 88% — Raise decay (≈6) so falloff stays near the source instead of washing out distant elements, then balance intensity (≈5) and tint the light for mood.
- 8:54→9:26 [PointLight] · explained · scripted · Example · 82% — A second light placed off to one side in a contrasting color adds rim/fill separation; brighten it to gauge its effect, then pull intensity back down.
- 9:26→9:45 [OrbitCamera] · explained · scripted · Parameters · 82% — Its distance and orbit-speed defaults give an automatic circling shot out of the box; just nudge distance for a closer or wider framing.
- 9:45→9:53 [RenderTarget] · passing · scripted · Concept · 70% — Used as the entry point of the post stack, here noted for adding multi-sampling anti-aliasing to the rendered scene.
- 9:53→10:29 [DepthOfField] · explained · scripted · Parameters · 82% — Pin it and darken/neutralize the blur so bright out-of-focus elements recede, pulling attention to the sharp center of frame.
- 10:29→10:36 [Glow] · explained · scripted · Tip · 80% — Adds the HDR bloom that makes emissive surfaces bleed light; ease the amount back so highlights glow without blowing out.
- 10:36→12:09 [ColorGrade] · explained · scripted · Parameters · 78% — Push brightness and tune the overall look to set scene mood; can be dialed in by feel rather than by exact values.
- 12:09→12:41 [Grain] · explained · scripted · Parameters · 78% — Set a non-zero speed (≈55) so the grain animates instead of sitting static, then keep the amount low so it's barely perceptible.
- 12:41→12:58 [ChromaticDistortion] · explained · scripted · Gotcha · 80% — Defaults run far too strong; cut the size way down (≈0.025) and reduce the colorize amount to avoid a greenish tint on edges.
- 12:58→13:22 [PerlinNoise3] · in-depth · scripted · Example · 85% — Feed it into a transform's rotation input to drive smooth pseudo-random orientation; keep the output range small (≈0.2) so motion is subtle.
- 13:22→13:32 [Transform] · explained · scripted · Example · 80% — Sits on a point branch so its rotation can be animated externally, turning a static cloud into one that slowly tumbles.
- 13:32→13:51 [Time] · explained · scripted · Concept · 78% — Wire it into a noise operator's time input to give an otherwise static field continuous animation over playback.
- 14:08→14:14 [PerlinNoise3] · explained · scripted · Parameters · 80% — Lower the octave count (1–2) to trade fine detail for smoother, more continuous large-scale movement.
