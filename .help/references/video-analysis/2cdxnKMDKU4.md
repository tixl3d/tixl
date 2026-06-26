---
video: 2cdxnKMDKU4
type: tutorial
date: 2022-07-14
title: Tooll 3 Tip#011 - Combining Point Buffers
duration: 0:02:31
focusesOn: [CombineBuffers]
---

A short tip showing that each point operator emits its own GPU point buffer, and how keeping the original and modified buffers around lets you wire them together — pairing positions to draw lines, then scattering meshes across the same points.

## Mentions
- 0:09→0:20 [RadialPoints] · passing · scripted · Example · 75% — Starting point for a point cloud: emits a ring of points you can immediately visualise by drawing it as a circle.
- 0:22→0:32 [AddNoise] · explained · scripted · Example · 80% — Drop it after a point generator to warp the positions, bending an otherwise regular point layout into curvy organic shapes.
- 0:32→1:56 [CombineBuffers] · in-depth · scripted · Concept · 72% — Why each stage keeps its own point buffer — the original ring, the noise-warped copy, the line set, the mesh-instanced set — and how merging those buffers lets you layer point geometry up stage by stage instead of overwriting it. (Shown but never named in the narration; this is its dedicated tip.)
- 0:32→0:58 [ui:EvaluationContext] · in-depth · scripted · Concept · 85% — Each point operator outputs a separate GPU buffer rather than overwriting in place, so the unmodified and modified point sets both survive downstream — looks wasteful but lets you combine the two stages.
- 0:58→1:13 [DrawLines] · explained · scripted · Example · 80% — Pair an original point set with a displaced copy of the same points and connect them, turning two buffers into drawn line segments between matched positions.
- 1:13→1:27 [ui:Graph] · passing · scripted · Tip · 65% — Wrapping a working chain into a group keeps the buffer-combining setup tidy and lets you pin and animate the whole unit at once.
- 1:27→1:43 [Transform] · passing · scripted · Example · 70% — Offsetting one buffer with a transform pushes the line endpoints away from their source points, opening a gap you can drive for animation.
- 1:40→1:56 [DrawMeshAtPoints] [CubeMesh] · explained · scripted · Example · 80% — Reuse the same point buffer to instance a mesh at every point; feed a [CubeMesh] as the instanced geometry and scale it down since default instances come out oversized.
