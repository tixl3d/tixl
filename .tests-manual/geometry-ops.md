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
Add `[CubeGeometry]`, `[GeometryToMesh]` and `[DrawMesh]` and connect them
in that order. Pin the `[DrawMesh]` output.

**Expected:**
- A lit, solid cube renders (not inside-out, no missing faces).
- The wire between the geometry ops is teal-green; the compiled mesh wire is red.

## Step: Beveling

**Action:**
Insert a `[BevelGeometry]` between `[CubeGeometry]` and `[GeometryToMesh]`.

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
(Distance ~0.3) into the `[GeometryToMesh]`.

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

## Step: Toggling interior cells and preserving openings

**Action:**
Use a closed cube extending from -1 to 1 on each axis with seven seed points: the center and one at each of ±0.5 on X, Y and Z.
Feed it into [VoronoiFracture] with **Async** disabled, then toggle **FillInterior** and inspect the output parts.

**Expected:**
- With **FillInterior** enabled, seven fragments are present, including the center cell.
- With **FillInterior** disabled, six fragments remain and the center cell is absent.

**Action:**
Remove the cube's top face and use two seed points at (-0.5, 0, 0) and (0.5, 0, 0).
Toggle **FillInterior** again and inspect the pieces' open edges.

**Expected:**
- Both settings preserve the top openings; enabling **FillInterior** does not seal the input's missing face.

## Step: Fracturing a concave shape

**Action:**
Feed a concave solid into `[VoronoiFracture]` instead of the cube: either
`[StringToCurves]` -> `[ExtrudeCurves]` (text `Xg8`, Depth 0.2) or
`Lib:meshes/ring-bevelled.obj` via `[LoadObjGeometry]`. Scatter 10 to 40 seeds
across it and pin the fracture's output.

**Expected:**
- The geometry output view reports 0 boundary edges and the same volume as the
  input (a plane cuts such a shape in several separate loops, and each has to
  become its own cap).
- No chunk shows a huge flat face spanning the whole model.
- Isolating single chunks with `[FilterGeoPartsByIndex]` shows closed pieces.

## Step: Sweeping the seed orientation

**Action:**
Feed `[VoronoiFracture]` (input: `[StringToCurves]` text `X` -> `[ExtrudeCurves]`)
with 100 seeds from `[RadialCPoints]`, radius 1, and animate the `Axis` with a
`[PerlinNoise]` so the ring of seeds tumbles through the glyph.

**Expected:**
- At most orientations the geometry output view reports 0 boundary edges.
- Known limit: a few orientations put two seeds so close that their cell is a
  hair-thin slab; the cap of such a slab across the concave X shows overlapping
  faces (boundary edges, exact volume). This is the open item noted in the plan,
  not a regression to report.

## Step: Fracturing an open mesh

**Action:**
Point `[LoadObjGeometry]` at `examples:meshes/low-poly-people/female-dancing.obj`
and fracture it.

**Expected:**
- A warning appears in the log naming the number of open and non-manifold edges
  in the input, and it appears once per input change rather than every frame.
- The chunks have holes; this follows from the input, not from the operator.

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
Feed the `[VoronoiFracture]` into a `[GeometryToMeshChunks]`. Connect its `Buffers`
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
  the seed count re-fits the draw without a visible hitch.
- Changing the `[ScatterPointsInVolume]` Seed (same seed count) redraws every
  chunk as a closed piece; no torn slivers from the previous fracture remain.
- Routing `Points` through `[TransformCPoints]` and `[UploadToGpu]` instead of
  `GPoints` through `[TransformPoints]` gives the same picture for the same
  Scale and Rotation values.

## Step: Placing geometry at points

**Action:**
Scale the beveled cube down with a `[TransformGeometry]` (Scale ~0.15), feed it
into a `[PlaceGeometryAtPoints]` together with a `[ScatterPointsInVolume]`
(Count 40, Size 2), and render the result through `[GeometryToMesh]`.

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

## Step: Text as lines

**Action:**
In a new graph add `[StringToCurves]` (default text "TiXL", font
`Lib:fonts/Inter-Variable.ttf`), connect it to `[CurvesToPoints]`, then
`[DrawLines]`.

**Expected:**
- The word renders as outlines, one closed loop per contour (the i-dot and the
  counter of the letters are separate loops).
- Changing `Text`, `Size`, `Alignment` or `LineSpacing` updates immediately;
  the geometry output view lists one part per glyph with CodePoint/CharIndex
  attributes.
- Picking a different font file in the `Path` asset picker (Font asset type,
  `fonts` subfolder) swaps the outlines.

## Step: Text spacing, position and point orientation

**Action:**
In the text-as-lines graph set `LetterSpacing` to 0.2, then toggle `Alignment`
between Left, Center and Right. Drag the move gizmo of `[StringToCurves]` in the
output. Then insert `[DrawPoints]` or oriented sprites after `[CurvesToPoints]`
(or check the point rotations in the list view of `[CurvesToCPoints]`).

**Expected:**
- The letters spread evenly; with Center and Right the lines stay centered and
  right-aligned. Negative values tighten the text.
- The gizmo moves the whole text block and writes to `Position`.
- Every point's Z axis faces away from the letter's ink: outward on outlines,
  into the hole on the counters of "o", "a" and "e".

## Step: Offsetting outlines

**Action:**
Insert `[OffsetCurves]` between `[StringToCurves]` and `[CurvesToPoints]`. Sweep
`Offset` from -0.03 to 0.05 and switch `Join` between Miter, Round and Bevel.
Then feed the offset curves into `[ExtrudeCurves]` instead.

**Expected:**
- Positive values grow every letter and shrink its holes; negative values thin
  them. Corners of T, X and L stay sharp and closed with Miter, round with Round
  and cut flat with Bevel.
- Lowering `MiterLimit` towards 1 turns the sharpest miters (the tip of A) into
  bevels.
- The filled `[ExtrudeCurves]` result has no gaps at corners.
- Very large negative offsets can fold thin strokes into small loops; this is a
  known limit.

## Step: Text as solid mesh

**Action:**
Replace `[CurvesToPoints]` and the line ops with `[ExtrudeCurves]` ->
`[GeometryToMesh]` -> `[DrawMesh]`. Set `Text` to `TiXL Xg8&@`.

**Expected:**
- With `Depth` 0 the text is a flat fill with correct holes (g, 8, @) and the
  overlapping strokes of the X fill as one solid.
- With `Depth` 0.2 the geometry output view reports 0 boundary and 0
  non-manifold edges and a positive volume; walls are flat shaded.
- `Bevel` 0.01 rounds the edges smoothly; `BevelSegments` 1 gives a chamfer.
  A bevel much larger than the stroke width (e.g. 0.05 on "T") visibly folds the
  outline — this is the documented limit, not a bug.
- With a coarse `Tolerance` (0.03) the round sides of o and 8 still shade
  smoothly while the corners of T and X stay hard; lowering `SmoothAngle` below
  the outline's corner angles makes the round sides facet too.
- `FlatShading` facets every wall and bevel; caps are unaffected.
- `Weight` 0.03 gives a faux bold (sharp corners, counters shrink evenly),
  -0.01 a lighter cut; both stay closed (0 boundary edges). Beyond about 0.05
  thin counters close up, which is expected.
- `EvenOdd` turns the X's overlap into a hole; front/back/side flags remove the
  respective faces.
- `[ColorFacesFromAttribute]` with the part-index option colors each letter
  differently; the `IsSide` primitive attribute separates walls from caps.

## Step: Set an attribute per letter

**Action:**
Connect [StringToCurves] (text `TiXL`) -> [CustomGeometryAttribute] (Domain Parts, Name `W`, Code
`return i == 1 ? 1f : 0f;`) -> [TransformGeometry] (Translation Y 0.1, Selection `W`), and look at the Output Window.

**Expected:**
- Only the `i` moves up; the other letters stay.
- In the Table of [CustomGeometryAttribute]'s output, **Parts** shows a `W` column with 1 for the `i` and 0 elsewhere.
- A typo in Code gives the Code field an error border with the snippet's line and error below it; the last working values stay.

## Step: Color letters from an attribute

**Action:**
Connect [StringToCurves] (text `TiXL`) -> [CustomGeometryAttribute] (Domain Parts, Name `W`, Code
`return i * 0.25f;`) -> [ColorGeometry] (Domain Parts, Attribute `W`, Mapping FromStart, a black-to-white gradient)
-> [CurvesToCPoints] (Sampling Points), and look at the Output Window.

**Expected:**
- The four letters get four grey levels, from black for the `T` to light grey for the `L`.
- Switching Domain to Points and Input Mode to Index gives a ramp along all points; Random gives noise that changes with Seed.
- With an empty or misspelled Attribute the colors stay unchanged and the op shows a warning.
- An upstream [SelectGeometry] (Every 2) recolors only every second point.

## Step: Subdivide a cube

**Action:**
Connect [CubeGeometry] -> [SubdivideGeometry] and look at the Output Window, Stats tab.

**Expected:**
- Smoothness 0 with 1 iteration gives 26 points and 24 quads; the volume stays 1 and the cube looks unchanged.
- Smoothness 1 gives a rounded shape (volume about 0.42) that shrinks with more iterations; Boundary edges stays 0 and the shading is smooth. 0.5 lies in between.
- Insert [SelectGeometry] (Domain Points, Count 3) before it and set SmoothnessAttribute to `Selection`: only the region around those points rounds off.
- With [StringToCurves] as input the curves pass through unchanged.

## Step: See and limit a face selection

**Action:**
Connect [CubeGeometry] -> [SubdivideGeometry] (Linear) -> [SelectGeometry] (Domain Primitives, Every 5) ->
[TransformGeometry] (Translation Y 0.2), select [SelectGeometry] and look at the Output Window.

**Expected:**
- The selected faces are outlined in white with their corners as dots; edges hidden behind the cube are faint.
- Count 2 keeps only two faces from Offset; animating Offset moves them around the cube.
- On [TransformGeometry], Selection shows `<Auto: Selection>`. Typing opens a list with `<Auto>`, `<All>` and the float attributes; `<All>` moves the whole cube.

## Step: Read attributes in a snippet

**Action:**
Connect [CubeGeometry] -> [CustomGeometryAttribute] (Domain Points, Name `W`, Code `return p.Y;`) ->
[SelectGeometry] (Mode Custom) -> [TransformGeometry] (Translation Y 0.2).

**Expected:**
- Code `return @W > 0;` moves the top four corners.
- Domain Primitives with `return @W > 0.4f;` selects only the top face; Domain Vertices with `return @Normal.Y > 0.5f;` too.
- `return @Nope > 0;` moves nothing and the op warns that the attribute is missing.
- Add a [CustomGeometryAttribute] (Domain Primitives, Name `FN`, Type Vector3, Code `return Vector3.Normalize(@Normal);`)
  after the cube. Below its Code field a dimmed note says `@Normal averaged from vertices`.
- [SelectGeometry] (Domain Primitives) with `return @FN.Y > 0.9f;` moves only the top face.

## Step: Sample a field into an attribute

**Action:**
Connect [CubeGeometry] -> [SubdivideGeometry] (Smoothness 0, Iterations 3) -> [AttributeFromField] (Domain Points,
Name `N`, Field from a [NoiseField]) -> [ColorGeometry] (Attribute `N`) and look at the Output Window.

**Expected:**
- The cube shows the noise as a gradient; changing the noise changes the colors.
- Without a connected Field the op warns and passes the geometry through.

## Step: Flat text and a depth wave

**Action:**
Connect [StringToCurves] (text `TiXL`) directly to [GeometryToMesh] -> [DrawMesh].

**Expected:**
- The letters render flat and filled, holes included; turning FillCurves off on [GeometryToMesh] hides them and the op
  warns that there is nothing to draw.
- [StringToCurves] -> [FillCurves] -> [GeometryToMesh] looks the same; the Stats of [FillCurves] show polygons, one part
  per letter.
- Insert [CustomGeometryAttribute] (Domain Parts, Name `Depth`, Code `return 0.5f + 0.5f * Sin(i * 0.8f + A);`) and
  [ExtrudeCurves] (DepthAttribute `Depth`) before [GeometryToMesh]: each letter gets its own depth; animating A moves
  the wave. A misspelled DepthAttribute warns and extrudes all letters equally.
- With KeepCurves on [ExtrudeCurves], the front caps don't flicker: [GeometryToMesh] doesn't fill the kept curves again.

## Step: Segmented cube and curves through mesh ops

**Action:**
Set Segments of [CubeGeometry] to 2, 1, 3 and look at the Stats. Then connect [ExtrudeCurves] (KeepCurves on) to
[BevelGeometry], [TriangulateGeometry], [SeparateLooseParts] and [VoronoiFracture] in turn.

**Expected:**
- The cube has 24 points and 22 quads, Boundary edges 0 and volume 1; 1, 1, 1 gives the plain cube.
- Each op's output still contains the letter outlines as curves (Stats, Table Primitives), after all parts.
- After [TriangulateGeometry], the IsSide primitive attribute has one value per triangle.

## Step: Adaptive subdivision

**Action:**
Connect [CubeGeometry] -> [SelectGeometry] (Domain Primitives, Offset 4, Count 1) -> [SubdivideGeometry]
(Smoothness 0, Iterations 3, LevelAttribute `Selection`) and look at the Output Window, Stats tab.

**Expected:**
- Only the top face is refined: 69 polygons (64 + 5), Boundary edges 0, volume 1.
- Smoothness 1 rounds the top while the border to the other faces stays closed (Boundary edges 0).
- With [GeometryToMesh] -> [DrawMesh] after it, the cube's edges stay hard (SmoothAngle 60); SmoothAngle 180 blends
  the shading across them.
- A [SelectGeometry] Custom snippet `return i == 4 ? 0.5f : 0f;` with Iterations 4 refines the top face twice (21 polygons).
- With LevelAttribute empty, a [CubeGeometry] of Size 4, 1, 1 and MaxEdgeLength 0.6 gives far fewer faces than
  without MaxEdgeLength, with the long sides refined more than the ends.

## Step: Merge overlapping curves

**Action:**
Connect [StringToCurves] (text `TiXL`) -> [MergeCurves] -> [ExtrudeCurves] and look at the Stats of each.

**Expected:**
- Without overlaps, [MergeCurves] keeps 5 curves in 4 parts; the dot of the `i` stays in the `i`'s part.
- With LetterSpacing -0.2 on [StringToCurves], the letters merge into one outline in one part, and the extrusion is
  closed (Boundary edges 0) without walls inside the overlaps.
- Xor turns overlaps into holes; Intersect keeps only the overlapping areas.
- A [CubeGeometry] as input passes through with the warning that there are no closed curves.

## Step: Simple extrude without triangulation

**Action:**
Connect two overlapping `[NGonCurves]` through `[CombineGeometry]` -> `[MergeCurves]`, insert `[Extrude]` and look at
its Stats. Compare the same input through `[ExtrudeCurves]`. Then switch `Front`, `Back` and `Sides` off one at a time
and set `Depth` to 0.

**Expected:**
- `[Extrude]` outputs 2 cap polygons plus one quad per outline segment (12 polygons for a 10-point outline); no
  triangles.
- Boundary edges 0, non-manifold edges 0, and Volume = outline area × Depth; the solid runs from Z 0 to −Depth.
- Caps stay single N-gons and walls stay quads, so a `[BevelGeometry]` after it bevels whole edges instead of triangle
  edges.
- `Front`, `Back` and `Sides` off remove the respective faces; `Depth` 0 gives the flat caps only; a `[CubeGeometry]`
  as input reports “No closed curves to extrude.”
- A glyph with a hole (`o`) renders with a real through-hole: the hole contour is spliced into the outline, so the cap
  stays one N-gon. The stats stay watertight (0 boundary and 0 non-manifold edges) and Volume is that of the ring; the
  two bridge slits show as thin seams on the caps. An `8` has two holes. `[ExtrudeCurves]` gives the same holes without
  the slits.
- The two bridge edges per hole carry the `IsBridge` edge attribute and no walls are built there; a shape without
  holes has no `IsBridge` attribute and 0 boundary edges.
- A concave outline renders without folds: `[GeometryToMesh]` triangulates concave N-gons with a constrained Delaunay
  triangulation instead of a fan, so a cap like the merged outline keeps its notch. Compare with `[TriangulateGeometry]`
  on the same geometry, which still fans and folds there.

## Step: Weld duplicated points

**Action:**
Load an .obj whose positions are listed once per face, e.g. one exported by [WriteObj] after [GeometryToMesh], with
[LoadObjGeometry] -> [MergeVertices] (Tolerance 0.0001), and look at the Stats of each.

**Expected:**
- [LoadObjGeometry] has one point per `v` line and many more vertices; its Boundary edges show that no faces share
  edges.
- After [MergeVertices], Points drops to the number of unique positions, Vertices stays the same, and Boundary edges
  shrink because the faces now share edges through the welded points. The status reports how many points were merged.
- With the output of [FillCurves] as input, the status says no points to merge: the fill already shares its points.
- A Tolerance larger than the spacing of the mesh collapses thin faces; 0 merges only exactly coincident points.

## Step: Merge duplicate mesh vertices

**Action:**
Connect [StringToCurves] (text `TiXL`) -> [FillCurves] -> [GeometryToMesh] -> [WriteObj], export once with Merge
Duplicate Vertices off and once with it on, and count the `v` lines of both files.

**Expected:**
- With the option off, the export has one vertex per face corner, several times the Points count of [FillCurves].
- With it on, the exported vertex count equals the Points count of [FillCurves]; the faces, UVs and normals are
  unchanged and the render looks the same.
- Corners whose position, normal, UVs or color differ, e.g. at a UV seam, stay separate vertices.

## Step: Lattice caps

**Action:**
Connect [StringToCurves] (text `TiXL`) -> [ExtrudeCurves] (Lattice 0.05) -> [SubdivideGeometry] (Smoothness 1,
Iterations 2) -> [GeometryToMesh] -> [DrawMesh], and compare with Lattice 0.

**Expected:**
- With Lattice, the front and back caps are a grid of squares with small pieces along the outline; without it, long
  thin triangles span the letters. The subdivided result shows no streaks across the caps.
- The Stats of [ExtrudeCurves] and [SubdivideGeometry] show Boundary edges 0; the volume of [ExtrudeCurves] is the
  same with and without Lattice.
- With a Bevel, or a Lattice so small that it needs more than 40000 cells, the op warns and builds the plain caps.
- [FillCurves] with Lattice gives the same grid as a flat surface.

## Step: Inset the filled outline

**Action:**
Connect [StringToCurves] (text `TiXL\naeBC`) -> [SubdivideCurves] (MaxEdgeLength 0.05) -> [FillCurves] (Inset 0.02)
-> [GeometryToMesh] -> [DrawMesh] and look at the Stats of [FillCurves].

**Expected:**
- A ring of quads runs along every outline, with the fill inside it; the outer bounds and the silhouette don't change.
- The Stats show no non-manifold edges; boundary edges are the outline edges, plus a few where a tiny counter collapses.
- Set Lattice 0.05: the cap inside the ring becomes a grid, but the ring stays one quad per outline edge: the cap's
  border cells snap to the nearest inset point instead of adding points on the ring. No non-manifold edges appear.
  Without the ring, the letters show long triangles from the outline.
- Set Inset 0.5 on letters with a thin stem: the op warns that the inset was limited, and the rings stop before
  opposite outlines overlap.
- Weight still offsets the whole letter first; the ring is built inside the offset outline.
- Add [ColorFacesFromAttribute] (Attribute `IsInset`, Colors white and blue): the ring faces become blue and the cap
  stays white; with Inset 0 every face stays white.
- With Lazenby Computer Liquid and text `Cc`, both letters get a complete inset ring: hairline edges in the outlines
  are welded before insetting.

## Step: Constrained Delaunay caps

**Action:**
Connect [StringToCurves] (text `aeBC`) -> [SubdivideCurves] (MaxEdgeLength 0.05) -> [FillCurves] -> [GeometryToMesh]
-> [DrawMesh]. Toggle ConstrainedDelaunay and compare, also with Inset and Lattice on.

**Expected:**
- With ConstrainedDelaunay on, the caps show fewer and less extreme long thin triangles than the default fill; holes
  stay open and the Stats keep the same area.
- Turning it off restores the default fill. With Lattice and ConstrainedDelaunay on, the cap keeps the lattice's point
  density but is triangulated into better-shaped triangles instead of quads; with Lattice alone it stays a grid of
  quads. With Inset the cap still meets the ring without gaps or non-manifold edges. Where the inset rings cross, the
  op warns and falls back to the regular cap (the lattice grid if it is on, otherwise the default fill).
- On very thin stems some slivers can remain because the outline points are fixed; the op does not warn.

## Step: Polygon edges as lines

**Action:**
Connect [CubeGeometry] -> [CurvesToPoints] -> [DrawLines] and set PolygonEdges.

**Expected:**
- None draws nothing (no curves), All and Sharp draw the 12 cube edges.
- After [SubdivideGeometry] (Smoothness 0, Iterations 2), All draws the whole grid, Sharp still only the cube's edges.
- On [ExtrudeCurves] of a text, Sharp outlines the letters on both caps and the corners of the walls, without lines
  across the caps, also with Lattice.

## Step: Filter by a selection

**Action:**
Connect [CubeGeometry] -> [SelectGeometry] -> [FilterGeometry] and look at the Stats of [FilterGeometry].

**Expected:**
- [SelectGeometry] Domain Points, Count 2: 2 points and no primitives remain (loose points).
- Domain Primitives, Offset 4, Count 1: the top face with its 4 points remains; Invert keeps the other 5 faces.
- A misspelled Attribute warns and passes the geometry through.
