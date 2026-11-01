---
id: points-on-mesh
title: PointsOnMesh Distribution
scope: operators
tags: [user, edge]
added: 2026-11-01
added-in-version: 4.3
prerequisites:
  - A writable user project with an empty composition is open.
---

`[PointsOnMesh]` scatters points over the triangles of a mesh, weighted by their area. This set checks that no triangle is skipped, that changing the seed re-scatters the whole point cloud, and that the optional extra attributes are written correctly.

## Step: Points spread over both triangles of a quad

**Action:**
Add `[QuadMesh]` and leave `Segments` at 1×1, so the quad consists of two triangles. Connect its `Data` output into a `[PointsOnMesh]`, set `Count` to about 200, and connect its `ResultPoints` output into `[DrawPoints]`. Look at the quad from the front in the output window.

**Expected:**
- Points cover both halves of the quad, roughly evenly, because both triangles have the same area.
- The diagonal between the two triangles is not a border with one empty side.

## Step: Every point moves when the seed changes

**Action:**
Animate the `Seed` input with small changes, or step it by hand, while leaving the mesh and the point count unchanged.

**Expected:**
- The whole point cloud re-scatters with each new seed.
- No point keeps the same position from one seed to the next.
- Both halves of the quad keep receiving points.

## Step: Writing UVs into FX1 and FX2

**Action:**
In the setup from the first step, set `[PointsOnMesh]`'s `Extra Attributes` to `TexCoord` and set `[DrawPoints]`'s `ScaleFactor` to `F1`. Then switch `ScaleFactor` to `F2`.

**Expected:**
- With `F1`, the points grow steadily along the U direction of the quad and shrink along the other.
- With `F2`, the size gradient follows the V direction instead.
- Switching `Extra Attributes` back to `Default` makes all points the same size again.

## Step: Writing the vertex selection into FX1 and FX2

**Action:**
Insert `[SelectVertices]` between the quad and `[PointsOnMesh]`, leave `VolumeShape` at `Sphere`, and reduce `Stretch` until only part of the quad is selected. Set `[PointsOnMesh]`'s `UseVertexSelection` to off, `Extra Attributes` to `VertexSelectF1`, and `[DrawPoints]`'s `ScaleFactor` to `F1`. Then switch `Extra Attributes` to `VertexSelectF2` and `ScaleFactor` to `F2`.

**Expected:**
- Points inside the selected region are larger than points outside it, with a smooth transition across the selection border.
- The size pattern stays the same when both inputs are switched to `F2`.
- Mixing the two (e.g. `VertexSelectF1` with `ScaleFactor` set to `F2`) leaves the point sizes uniform.
