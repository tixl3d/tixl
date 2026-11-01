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

`[PointsOnMesh]` scatters points over the triangles of a mesh, weighted by their area. This set checks that no triangle is skipped and that changing the seed re-scatters the whole point cloud.

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
