---
id: geometry-ops
title: Procedural Geometry Ops
scope: geometry
tags: [regression]
added: 2026-09-02
added-in-version: 4.3
prerequisites:
  - An empty project is open.
related-help:
  - ../.help/docs/using/ProceduralGeometry.md
---

Verifies the CPU procedural geometry chain: generating, beveling, transforming and
compiling geometry for rendering.

## Step: Building the basic chain

**Action:**
Add `[CubeGeometry]`, `[GeometryToMeshBuffers]` and `[DrawMesh]` and connect them
in that order. Pin the `[DrawMesh]` output.

**Expected:**
- A lit, solid cube renders (not inside-out, no missing faces).
- The wire between the geometry ops is teal-green; the compiled mesh wire is red.

## Step: Beveling

**Action:**
Insert a `[BevelGeometry]` between `[CubeGeometry]` and `[GeometryToMeshBuffers]`.

**Expected:**
- All twelve edges and eight corners show smooth rounded bevels.
- No dark notches, spikes or holes at the corners.

## Step: Animating the bevel width

**Action:**
Drag the `Width` parameter continuously between 0 and 0.3.

**Expected:**
- The bevel follows interactively at full frame rate.
- The width stops growing when the automatic clamp kicks in; no self-intersections appear.

## Step: Chamfer and flat shading

**Action:**
Set `Segments` to 1 and `Roundness` to 0, then enable `FlatShading`.

**Expected:**
- The bevel becomes a straight chamfer.
- With `FlatShading` enabled, every face reads as a hard plane (no smooth gradients
  across the bevel strips).

## Step: Round corners

**Action:**
Set `Segments` to 4 and `Roundness` to 1, then enable `RoundCorners`.

**Expected:**
- Corners change from a flat fan into a spherical patch: the edge profile
  continues through the corner in concentric rings.
- No spikes at the corners, even when `Width` is dragged up to the clamp.

## Step: Custom scalar field

**Action:**
Replace the field input of `[DisplaceGeometry]` with a `[CustomScalarField]` and set
its `Code` to `return Sin(p.X * B) * Sin(p.Y * B) * Sin(p.Z * B) * A;` with `A` = 1
and `B` = 8.

**Expected:**
- The mesh shows a regular sine bump pattern; editing `A`/`B` updates it live.
- Entering invalid code logs a warning in the console (with the snippet line
  number) and the last working field keeps rendering.

## Step: Voronoi fracture

**Action:**
Feed the beveled cube into a `[VoronoiFracture]`, its `Points` from a
`[ScatterPointsInVolume]` (Count ~12), then through an `[ExplodeGeometry]`
(Distance ~0.3) into the `[GeometryToMeshBuffers]`.

**Expected:**
- The cube breaks into chunks that separate with Distance; each chunk is closed
  (no holes), with smooth beveled outer surfaces and flat cut faces.
- Changing the scatter `Seed` produces a different, deterministic fracture.

## Step: Async computation

**Action:**
On a heavy setup (e.g. a fractured OBJ mesh), enable the `Async` parameter on
`[VoronoiFracture]` (and/or `[BevelGeometry]`), then drag upstream parameters.

**Expected:**
- The UI keeps its frame rate while dragging; the geometry snaps to the new
  result shortly after, showing the previous result in between.
- Switching `Async` off returns to immediate (blocking) updates with identical
  results.
- Rendering to a file waits for pending results (no stale frames in the export).

## Step: Centering geometry

**Action:**
Insert a `[CenterGeometry]` after a `[LoadObjGeometry]` with an off-center mesh
and set `Pivot` to (0, -0.5, 0).

**Expected:**
- The mesh moves so its bounding-box bottom center sits on the world origin.
- Pivot (0,0,0) centers the bounding box; other pivots pick the matching
  normalized position inside the box.

## Step: Progress bar

**Action:**
On a heavy async op (e.g. `[VoronoiFracture]` with many seeds on a dense mesh),
change a parameter so the computation takes more than about half a second.

**Expected:**
- After ~0.5s an orange progress bar appears at the bottom edge of the op's
  graph node and fills as the computation proceeds; quick updates show no bar.

## Step: Loading an OBJ file

**Action:**
Replace the `[CubeGeometry]` with a `[LoadObjGeometry]` pointing at
`Lib:meshes/camera-gizmo.obj` and set `Scale` to ~6.

**Expected:**
- The mesh renders with its original smooth/hard shading (normals from the file).
- Editing and re-saving the OBJ file reloads it automatically.
- Downstream geometry ops (bevel, fracture, explode) work on the loaded mesh.

## Step: Transforming before the bevel

**Action:**
Insert a `[TransformGeometry]` between `[CubeGeometry]` and `[BevelGeometry]` and
set its `Scale` to (2, 0.5, 1).

**Expected:**
- The stretched box shows even bevels on all edges (the bevel adapts to the
  transformed shape rather than being stretched with it).

## Step: Triangulation passthrough

**Action:**
Insert a `[TriangulateGeometry]` directly after `[CubeGeometry]`.

**Expected:**
- The rendered result is visually unchanged — triangulating before beveling adds
  no visible seams (flat edges between coplanar triangles produce no bevels).
