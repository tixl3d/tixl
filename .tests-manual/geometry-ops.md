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
- Selecting the `[VoronoiFracture]` shows "watertight" in the output view and
  a dash in every row of the parts table's open-edges column; the volume equals
  the beveled cube's. This holds up to hundreds of seeds.
- Changing the scatter `Seed` produces a different, deterministic fracture.
- With many seeds (hundreds) and a large Distance, fully interior fragments
  (all faces cut, no original surface) are present in the exploded cloud — the
  solid's inside is not hollow.
- Shrink the scatter `Size` to a small cluster inside the cube: the large outer
  chunks still have closed cut faces toward the cluster. Disabling
  `FillInterior` reproduces the old dark gaps (expected for that setting, which
  exists for open or non-manifold meshes).

## Step: Coloring fracture cuts per chunk

**Action:**
Insert a `[ColorFacesFromAttribute]` between `[VoronoiFracture]` and
`[ExplodeGeometry]`. Feed its `Colors` from a `[ColorsToList]` with three
distinct colors, set `Attribute` to "Part Seed Index" and keep `OnlySelected` on.

**Expected:**
- Each chunk's cut faces show one palette color (cycling through the list);
  the original beveled surface keeps its material color.
- With `OnlySelected` off, whole chunks take their palette color.

## Step: Slicing chunks and reading stats

**Action:**
Insert a `[FilterGeoPartsInBox]` after the `[VoronoiFracture]` and shrink its
`Size.Y` to a thin slab through the cluster; then add a `[GetGeometryStats]` on
the fracture output and look at its `Report`.

**Expected:**
- Only chunks whose pivot lies in the slab remain (`KeptCount` shows how many);
  `Mode` = KeepOutside shows the complement. The box has a gizmo when selected.
- The report lists point/face/triangle/part counts, size, bounds, volume,
  boundary edges (0 = watertight) and the evaluation time.

## Step: Geometry output view

**Action:**
Select (or pin) the `[VoronoiFracture]` itself, then the `[BevelGeometry]`.

**Expected:**
- The output window shows a stats view instead of an empty view: a headline
  with face and part counts, then points, triangles, size, bounds, volume, a
  surface line and the attribute list (name, type, domain).
- The surface line reads "watertight" in green for the beveled cube and lists
  boundary edges in the attention color for an open mesh.
- For the fracture, a scrollable parts table lists every chunk with face count,
  open edges (highlighted when non-zero), volume, seed index and pivot.

## Step: GPU chunks

**Action:**
Feed the `[VoronoiFracture]` into a `[GeometryToChunks]`. Connect its `Buffers`
to `Mesh`, `GPoints` to `GPoints` and `ChunkIndices` to `ChunkIndices` of a
`[DrawMeshChunksAtPoints]`; pin that op. Then insert a `[TransformPoints]`
between `GPoints` and the draw op and raise its `Scale` to ~1.8.

**Expected:**
- Without the transform, the chunk render sits exactly where `[DrawMesh]` shows
  the fractured cube.
- With the scale, the chunks fly apart from the cube center while each chunk
  keeps its shape; the CPU `[ExplodeGeometry]` is no longer needed for this.
- `ChunkCount` equals the number of fracture seeds.
- With a `[ColorFacesFromAttribute]` upstream, every chunk keeps its color in
  the chunk draw (vertex colors are used, multiplied with the point color).
- Dragging the `[TransformPoints]` scale is as smooth as `[DrawMesh]`; changing
  the seed count re-fits the draw table without a visible hitch.

## Step: Placing geometry at points

**Action:**
Scale the beveled cube down with a `[TransformGeometry]` (Scale ~0.15), feed it
into a `[PlaceGeometryAtPoints]` together with a `[ScatterPointsInVolume]`
(Count 40, Size 2), and render the result through `[GeometryToMeshBuffers]`.

**Expected:**
- 40 small cubes appear at the scattered positions; the output view shows 40
  parts and "watertight".
- Feeding points with orientation and scale (e.g. `[RadialCPoints]`) rotates and
  scales the copies; `UseOrientation` / `UseScale` off places them axis-aligned
  at unit size.
- Colored input points color the copies when `UseColor` is on.

## Step: Isolating a single chunk

**Action:**
Replace the `[FilterGeoPartsInBox]` with a `[FilterGeoPartsByIndex]` (`Start` 0,
`Count` 1) and step `Start` up with the arrow keys.

**Expected:**
- Exactly one chunk renders at a time and each step shows the next one;
  `PartCount` reports the total number of chunks.
- `Count` 0 shows everything from `Start` on; a negative `Start` counts from the end.

## Step: Bypassing geometry modifiers

**Action:**
Select the `[BevelGeometry]` and toggle its bypass (parameter window button or
the graph shortcut) several times, with the output window showing the fractured
result.

**Expected:**
- Every toggle takes effect on the next frame: bevels vanish and return, and the
  downstream fracture recomputes each time (the chunk count changes).

## Step: Async computation

**Action:**
On a heavy setup (e.g. a fractured OBJ mesh), enable the `Async` parameter on
`[VoronoiFracture]` (and/or `[BevelGeometry]`), then drag upstream parameters.

**Expected:**
- The UI keeps its frame rate while dragging; the geometry snaps to the new
  result shortly after, showing the previous result in between.
- Changing a parameter while a long computation is still running restarts it
  with the new values right away (e.g. lowering the seed count of a slow
  fracture doesn't wait for the slow result first).
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
