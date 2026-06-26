---
video: LjmnVL7azYw
type: tutorial
date: 2022-07-22
title: Tooll 3 Tip#017 - Cloning vs. Instancing Meshes
duration: 0:04:31
focusesOn: [DrawMeshAtPoints], [RepeatMeshAtPoints]
---

A short tip contrasting two ways to multiply a mesh across scattered points: baking copies into one large mesh (cloning) versus drawing the same mesh repeatedly per point (instancing), and the trade-offs in memory, performance, and what you can still modify afterward.

## Mentions
- 0:24→0:37 [RepeatMeshAtPoints] · explained · scripted · Concept · 85% — Bakes the source mesh into every scatter point, producing one combined mesh with all the new vertices and faces — a heavier buffer, but the result is real geometry you can keep editing.
- 0:51→1:24 [DrawMeshAtPoints] · explained · scripted · Performance · 85% — Cheaper on GPU memory since it re-draws one small mesh per point instead of uploading a multiplied buffer; the cost surfaces mainly when the per-point count is animated, while drawing usually dominates over the upload anyway.
- 1:28→2:01 [DisplaceMesh] · explained · scripted · Example — 80% — Chaining it after a baked, point-multiplied mesh distorts the whole combined surface as one continuous form, which is only possible because the copies are real vertices rather than repeated draw calls.
- 2:01→2:43 [DrawMeshAtPoints] · explained · scripted · Gotcha · 88% — Any deformation must sit upstream of it, before the mesh is drawn; a displace placed after instancing has no effect, and one placed on the single source mesh repeats the identical distortion at every point.
- 3:00→3:14 [MeshProjectUV] · passing · scripted · Example · 65% — Reprojecting UVs onto a freshly deformed, point-multiplied mesh so a subsequent material maps cleanly across the new surface.
- 3:14→3:34 [SetMaterial] [LinearGradient] · explained · scripted · Example · 75% — Feeding a gradient in as the default material so its bands read as stripes across the combined geometry; works because cloning yields a real mesh that carries material and UVs.
- 3:34→3:56 [SetMaterial] · passing · scripted · Tip · 60% — Driving full transparency plus alpha clipping on the material to visibly slice through the geometry — costly but a quick way to cut into a solid form.
