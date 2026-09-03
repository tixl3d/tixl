# Plan: CPU Procedural Geometry (Curves, Meshes, Parts, Chunks)

Status: **Phase 0 implemented (needs in-editor smoke test); Phases 1+ not started**
Last update: 2026-09-02

## Goal

Add a CPU-side procedural geometry layer: editable curve and N-gon mesh types with
flexible attributes and selections, compiled late into the existing GPU
`MeshBuffers`/chunk/point infrastructure. First vertical slice (zero new
dependencies): `CubeGeometry -> BevelGeometry -> GeometryToMeshBuffers -> DrawMesh`.
Early architecture validation: delegate field connections + the fracture demo
(beveled cube -> Voronoi fracture with field-driven cell density -> chunk explode).
Headline milestone: 3D text
(`String -> TextToCurves -> CurvesToMesh -> GeometryToMeshBuffers -> DrawMesh`).
Longer-term: shape-grammar-style generation (spaces, parts, deterministic randomness).

Origin: external design discussion (ChatGPT summary, 2026-09) evaluated against the
existing codebase. This plan records the TiXL-adapted conclusions, not the raw discussion.

## Key design decisions

### Reuse existing types wherever possible (gltf-anim precedent)

Following `Plan_GltfAnimation.md`'s "no new connection types" approach:

- **Slots / spaces / per-part instancing = `Point[]`** (`StructuredList<Point>`).
  Position/Orientation/Scale carry the placement (for spaces, Scale = box extents);
  `F1`/`F2` carry stable id/seed for deterministic randomness. No `Slot`, `SlotSet`,
  or `SpaceSet` types. Every existing point op (scatter, grid, noise, repeat) becomes
  a slot/space generator for free. Limitation: no shear in a space — acceptable,
  CGA-style split/repeat doesn't need it.
- **No `GeometryScene`.** Geometry stays shape-only (flat parts). Hierarchy,
  materials and names live on the scene layer — see `Plan_SceneDocument.md` for
  the format-neutral `SceneDocument`, the derived draw setup, and the
  `PickGeometryFromScene` / `GeometryToScene` bridge (superseding the earlier
  "it evolves `SceneSetup`" note).
- **Chunks**: promote `MeshChunkDef` from `LoadGltfScene.cs` into Core (`Core/Rendering`,
  next to `PbrVertex`) instead of adding a parallel chunk type. Keep the GPU struct lean
  (ranges; bounds/material only if culling/picking needs them) — transforms and color
  ride on the `Point[]` side channel, matching `_GetSceneDefinitionPoints` +
  `DrawMeshChunksAtPoints`.

### New Core data types (keep this list minimal)

| Type | Contents |
|---|---|
| `GeometryAttributes` / `AttributeDescriptor` | name, domain, typed dense buffer (`float[]`, `int[]`, `Vector2/3/4[]`). Shared by curves and meshes; one attribute-transfer mechanism. |
| `CurveGeometry` | cubic-bezier control points (SoA), `ContourOffsets/Counts`, per-contour `Closed` flag, attributes. |
| `MeshGeometry` | `Positions`, `FaceOffsets/FaceCounts/CornerVertexIndices` (N-gons), attributes, optional part table (contiguous face ranges per part + per-part pivot/id/seed). |

Domains: `ControlPoint`, `Segment`, `Contour` (curves); `Point`, `Corner`, `Edge`,
`Face`, `Part` (meshes). Edge topology is *derived* lazily from faces and cached —
not stored.

Design rules (match house performance rules): SoA typed buffers, no per-element
objects or dictionaries, name-to-`AttributeId` lookup once outside hot loops, spans
in kernels, preallocated/reused buffers, dirty-flag caching in every op (procedural
chains re-evaluate under animation).

### `Point[]` is a projection format, not the attribute store

`Point`'s fixed fields (`F1`/`F2` especially) must never become the attribute system —
they are the last-mile interchange with the existing GPU point ecosystem, the way
`PbrVertex` relates to `MeshGeometry`:

- CPU-side, attributes live in `GeometryAttributes` only — incl. `int[]` ids/seeds at
  full precision (float32 is exact only to ~16.7M; bit-casting payloads through floats
  risks the NaN separator convention).
- A point cloud with rich attributes = `MeshGeometry` with zero faces. No `PointGeometry`
  type.
- `GeometryToPoints` / `CurvesToPoints` / `GeometryToChunks` project explicitly:
  parameters choose which attributes land in `Color`/`F1`/`F2`. Data loss at this
  boundary is a per-op choice, never a design constraint upstream.
- GPU ops needing more than the Point struct use sidecar buffers sharing the point
  index (house idiom: skin-weight side buffer, `_GetSceneDefinitionPoints`'
  chunk-index buffer). The 64-byte `Point` stride never changes.
- Spaces/slots keep only id/seed in `F1`/`F2` (all that split/repeat/random need —
  and what keeps existing point ops usable on spaces). Richer per-space data joins
  back via a source-point-index part attribute stamped by `PlaceGeometryAtPoints`.

Two contracts every op must honor:

- **Topology-modifying ops interpolate all attributes** (decimate, fracture,
  subdivide, extrude): collapsed/split elements blend their attribute values.
  Long-term this includes skin weights as point-domain attributes (today a side
  `BufferWithViews` in the gltf path), so modified meshes stay skinnable.
- **GPU->CPU crossings are explicit readback ops with dirty caching** (mesh, texture,
  points). Cheap for static/slow-changing sources; a per-frame readback is a
  deliberate budget decision, never hidden inside another op.

These types flow only through the graph (like `MeshBuffers`) — never serialized into
`.t3` input values. The frozen public interface is the operator parameter sets, which
keeps the interface-stability audit small. Run the audit (AGENT_INSTRUCTIONS
"Interface stability") on `AttributeDescriptor` and the part table before Phase 2 ships.

### Curves: cubic bezier canonical, flatten late

- Canonical segment type is the cubic bezier (fonts/SVG are natively cubic/quadratic;
  quadratic upgrades losslessly; polygons are degenerate cubics — e.g. slicing output).
  Catmull-style through-point curves are a *constructor* (reuse `BezierPointSpline`
  tangent logic), not a second storage format. No NURBS.
- Flattening to `Point[]` (with `Point.Separator()`) is an explicit late op — the
  compatibility bridge to all existing draw/point ops. Introduce one shared
  contour-iteration helper there (Phase 0 has already unified the separator
  convention on `Scale.x`-NaN by then).
- Give contours and control points stable indices so a future curve editor (or an
  `EditCurve` override op) can attach without a format change. No path-editing UI in
  this plan.

### Selections are attributes, not a wire type

A selection is a named `float` attribute (0..1) on a domain — precedent:
`PbrVertex.Selection` already exists in the render vertex format. Consequences:

- Selection flows *with* the geometry through one connection; no index-list type to
  keep in sync with topology changes.
- Soft selection (falloff, volume gradients) is free; ops treat the value as a weight.
- Modify ops (`ExtrudeFaces`, `TransformGeometry`, `ColorFacesFromAttribute`, ...) take an
  optional selection (default: everything). Ops that create elements set the selection
  on the new elements (e.g. extrude selects the new cap faces) so chains compose.
- Deterministic randomness: random selection hashes
  `hash(globalSeed, elementId, opSeed)` on stable ids so unrelated graph edits don't
  reshuffle results.

Selection ops (Phase 2/3):

- `SelectGeometry` — Domain (Points/Edges/Faces/Parts), Mode (All, Random fraction+seed,
  ByIndexRange, ByAttribute compare, ByVolume sphere/box with falloff, ByNormal,
  Gradient: direction + falloff over element positions/part pivots,
  ByPoints: proximity to a `Point[]` input with radius/falloff — makes any animated
  point source a selection driver, e.g. a skeleton joint from a pose),
  Combine (Replace/Add/Remove/Intersect), Invert flag.
  CPU delegate fields (Phase 2) add a Field mode — `ScalarField` evaluated at
  element positions as selection weight. Shader-graph/3D-texture fields remain
  GPU-only (would need readback).
  Covers "random 10% of faces, then extrude". Split into per-mode ops later only if
  the parameter set gets unwieldy.
- Derived edge topology arrives with `BevelGeometry` in Phase 1; richer edge
  selection modes follow as needed.

## Phases

### Phase 0 — Separator convention cleanup ✅ (implemented 2026-09-02, needs in-editor smoke test)

Done, working-tree only (uncommitted):

- **`LegacyPoint` eliminated**: 63 `.hlsl` files migrated to `Point`
  (`.W`->`.FX1`, `.Stretch`->`.Scale`, point-typed `.Selected`->`.FX2`;
  `PbrVertex.Selected` untouched). Struct removed from `shared/point.hlsl`;
  orphaned `shared/point-legacy.hlsl` deleted (zero includers — its quaternion
  helpers live in `shared/quat-functions.hlsl`, incl. the `NAN` define).
- **Separator convention unified on `Scale.x`-NaN**: emitters converted
  (`TraceContourLines`, `GrowStrains`, `WrapPointPosition`, `AppendPoints`,
  `LoadObjAsPoints.cs` now uses `Point.Separator()`); consumers converted
  (`DrawTubes`, `DrawRibbons`, `ExtrudeCurves_alternative` — pure `Scale.x`).
  Emitters that leave `Scale` uninitialized now write `Scale = 1` on live points.
- **NaN-width ("hidden point") meaning fully retired**: `Scale.x`-NaN is the ONLY
  NaN convention — a separator/null point that is never drawn and breaks line
  strips. `shared/point.hlsl` documents it and provides `IsSeparator(Point)`,
  adopted at every separator check in Lib (52 call sites, ~30 shaders) — no raw
  `isnan(*.Scale.x)` remains outside the helper. `SubdivideLinePoints`' local
  duplicate `IsSeparator` was removed (would have collided). Former NaN-width
  users converted: `TextSprites` empty-text fallback -> `Point.Separator()`;
  `GrowStrains` gates ungrown strains via `Scale`; `LegacyParticleSimulation`
  dead-slot flag moved to `Scale` (`FX1` is purely age now);
  `ResampleLinePoints`' redundant `sumF1 = NAN` removed (0/0 already nulls it).
- **Kept as-is**: `PointSimulation` (NaN-recovery guard — data validity, not
  semantics), examples (type/field rename only, no behavior change; their
  internal `FX1`-NaN pairings stay self-consistent).
  `examples/.../cynic/particles/add-point-cloud.hlsl` skipped —
  unrelated local struct, already broken (includes missing `shared/particle.hlsl`).
- **Cosmetics**: `sqrt(-1)` -> `NAN` define; local NaN consts removed
  (`ReflectionLines`, `PairPointsForGridWalkLine`); all 342 bare register
  bindings (`: t0;`) normalized to `: register(t0);` across 163 Lib shaders.
- Test set: `.tests-manual/point-separator-conventions.md`.
- Known behavior fixes (previously broken pairings): `DrawTubes`/`DrawRibbons` now
  break at `Point.Separator()` marks; `LoadObjAsPoints` edge separators visible.

### Phase 0b — Pre-Phase-1 cleanup ✅ (implemented 2026-09-02)

- **C# counterpart of the shader convention**: `Point.IsSeparator(in Point)` and an
  allocation-free `PointSegments`/`PointSegment` contour iterator in
  `Core/DataTypes/Point.cs`. Adopted in `DelaunayMesh`, `PrepareSvgLineTransition`,
  and `LineTextPoints` — the latter fixes a real bug: it validated points via
  `F1`-NaN while its own separators are `Scale`-NaN, so separator positions
  polluted orientation at contour ends. `CurvesToPoints` (Phase 2) reuses the
  iterator.
- **`ResourceManager.SetupBufferWithViews<T>(T[], ref BufferWithViews?)`** —
  replaces the buffer+SRV+UAV triple-call idiom; adopted in `CubeMesh` and
  `TorusMesh` (each drops two raw `Buffer` fields). `GeometryToMeshBuffers`
  (Phase 1) builds on it.
- **Dead code removed**: `ParticlePoint` struct incl. unused `Separator()`
  (`Core/DataTypes/ParticleSystem.cs`) — the last C# type with the legacy
  `W`/`Extra` field names, zero references; 11 commented-out `.W`/`.Stretch`
  lines across 8 shaders referencing fields that no longer exist.
- Verification: visual reference test suite (large coverage) — run by the
  maintainer against the combined Phase 0 + 0b diff.

### Precursor — Debug protocol subset (see `tixl-debug-protocol-plan.md`) ⬜

Decided 2026-09-02: before Phase 1, implement the scoped subset of the debug
protocol (audit, transport, read surface minus `getUiState`, dispatch basics,
CLI, reload). Rationale: the geometry phases' bottleneck is the in-editor
verification loop; the protocol lets bevel/fracture iterations run autonomously
(dispatch -> pumpFrames -> screenshot -> log/metrics) and makes the
`GeometryToMeshBuffers` buffer-reuse contract assertable via `getMetrics`.

### Phase 1 — Mesh core + cube/bevel slice ✅ (completed 2026-09-02)

Zero new dependencies — retires the attribute/topology/compile risks before fonts
and tessellation enter.

Done so far (protocol-verified: CubeGeometry -> GeometryToMeshBuffers -> DrawMesh
renders correctly, Size changes propagate, integration tests green):

- Core: `Core/DataTypes/Geometry/` — `MeshGeometry` (CSR topology:
  `FaceCornerOffsets` (FaceCount+1) + `CornerPointIndices` instead of the planned
  offsets+counts pair — same information, no redundancy to keep consistent),
  `GeometryPart` record struct, lazy cached `EdgeTopology` (unique edges w/ face
  adjacency + per-corner edge indices), `GeometryAttributes`/`GeometryAttribute<T>`
  (typed dense buffers per `AttributeDomain`, incl. reserved curve domains),
  `GeometryAttributeNames` consts. Sharing convention documented on the class:
  ops never mutate inputs, they build into their own reused output instance.
- Registration: `MeshGeometry` graph type (TypeRegistration) + distinct type color
  `UiColors.ColorForCpuGeometry` (teal-green) via `UiProperties.CpuGeometry`.
- Ops in `Operators/Lib/Symbols/geometry/`: `CubeGeometry` (6 quad N-gons,
  8 shared points, 24 corners with hard-edge normals + per-face UVs, CCW-from-
  outside winding — verified visually), `GeometryToMeshBuffers` (fan triangulation
  for convex faces, corner-attribute resolution with Newell face-normal fallback,
  per-face TBN via `MeshUtils.CalcTBNSpace` with degenerate-UV fallback, buffer
  reuse via `SetupBufferWithViews`).

**Milestone achieved (2026-09-02)**: `CubeGeometry -> BevelGeometry ->
GeometryToMeshBuffers -> [TransformMesh] -> DrawMesh`, animated bevel width at a
flat 60fps, all built and iterated over the debug protocol (5 reload cycles at
~2s each from first render to smooth-shaded result — the precursor investment
paying off exactly as intended).

- `BevelGeometry` (developed in `_agentTests` via `reload`, promoted to
  `Operators/Lib/Symbols/geometry/`): inset faces (bisector offset, w/sin(θ/2)),
  edge strips (quadratic-bezier profile around the inset edge, roundness lerps
  chamfer<->arc), corner fan patches (face-walk loop around each vertex,
  Newell-normal orientation fix, centroid-pulled-toward-vertex bulge). Smooth
  shading via a per-point normal map published as the corner-domain Normal
  attribute — inset points carry their face normal, so flat faces stay flat and
  strips blend seamlessly; first real exercise of the attribute pipeline.
  A `FlatShading` toggle (maintainer request) provides hard faceted normals by
  simply not publishing the Normal attribute - the compile step's per-face
  fallback does the rest. v1 limitations (documented): width clamped to 0.35x
  shortest edge, tiny silhouette nicks at extreme width+roundness. Promotion lesson: after moving an op
  out of `_agentTests`, reload the playground - its compiled assembly still
  carries the symbol and makes name lookups ambiguous.

**Phase complete (2026-09-02)**: `TransformGeometry` (shared-topology output,
normals rotated with inverse-scale correction for non-uniform scaling) and
`TriangulateGeometry` (fan, corner-attribute remap, part-range remap) shipped and
verified in a full protocol-driven chain (Cube -> Transform -> Triangulate ->
Bevel -> Compile -> Draw). Pleasant emergent behavior: beveling triangulated
geometry works — coplanar edges produce flat invisible strips, so only real
corners bevel. `GeometryAttributes.Add(attribute)` added for reference-sharing
unmodified buffers between geometries. Docs:
`.help/docs/using/ProceduralGeometry.md` + `.tests-manual/geometry-ops.md`.

**Phase 1 retrospective** (overlaps + low-hanging fruit):

- *Overlap*: the per-point-normal-map trick in `BevelGeometry` (points carry the
  normal, corners read from points) will recur in `CurvesToMesh` bevels (Phase 5)
  and any smooth-shaded generator — consider promoting a shared helper when the
  second user appears, not before. The attribute reference-sharing pattern
  (`Attributes.Add`) is what `SelectGeometry`/`SetGeometryAttribute` (Phase 5)
  will build on.
- *Low-hanging fruit now cheap*: `CylinderGeometry`/`SphereGeometry` are ~1h each
  (CubeGeometry is the template); a `WireframeGeometry` debug view via
  `EdgeTopology` -> `Point[]` line list would aid every future geometry op;
  `BevelGeometry` per-edge selection needs only the Phase 5 selection attribute
  plus a filter in its edge loop. Bevel-quality polish (extreme-width nicks)
  deferred to "bevel v2" in Phase 7+.

- Core: `MeshGeometry` (incl. part table), `GeometryAttributes`, derived edge
  topology (lazy, cached), slot-type registration + type color + output-window
  display (Editor).
- Ops (`Operators/Lib`):
  - `CubeGeometry` — 6 quad N-gon faces, corner-domain UVs and hard-edge normals.
    The existing `CubeMesh` stays untouched — its exact vertex layout is de-facto
    API for saved projects; retrofit onto the geometry path later only if the
    output is bit-identical.
  - `BevelGeometry` — v1 scope: uniform-width edge bevel, segments + profile
    roundness, all edges or an edge selection, clamped against overlaps, corner
    patches as fans. General miter/colliding-bevel handling is explicitly out of
    scope for v1.
  - `TransformGeometry`, `TriangulateGeometry` (explicit; also implicit at compile).
  - `GeometryToMeshBuffers` — compile: triangulate N-gons, corner normals with
    hard-edge policy, TBN, pack `PbrVertex[]` + `Int3[]`; reuse GPU buffers, only
    re-upload changed streams (precedent: `SkinMesh` shares index/chunk buffers).
- Milestone: `CubeGeometry -> BevelGeometry -> GeometryToMeshBuffers -> DrawMesh`,
  animated bevel width, flat frame times.

### Phase 2 — Delegate fields (validation slice) 🔶 (core implemented + verified 2026-09-02)

Pulled forward (maintainer decision, 2026-09): the delegate connection types are
the riskiest architectural bet in the plan and deserve validation right after the
attribute system exists, before the curve/text pipeline builds on settled ground.

Done: `Core/DataTypes/Geometry/Fields.cs` — `FieldSample` struct, the three
delegate signatures, wrapper slot types `ScalarField`/`VectorField`/`RemapCurve`
(callable + reserved `DescriptionNode`), registration + `ColorForCpuFields`
(yellow-green). Ops: `DistanceToPointsField` (StructuredList&lt;Point&gt; snapshot,
separator-aware via `Point.IsSeparator`, brute force with a spatial-grid note),
`GainAndBiasCurve` (Schlick), `RemapFieldValues` (field∘curve composition),
`ColorFromField` (corner Color attribute — note: NO built-in mesh shader consumes
`PbrVertex.ColorRgb` today, flagged as renderer follow-up below), and
`DisplaceGeometry` (points along accumulated Newell normals by field×amount;
recomputes smooth corner normals from the displaced shape when the input carried
a Normal attribute, otherwise stays faceted), and `NoiseField` (3D gradient noise
fBm, seeded, zero-centered ±Amplitude; verified as displacement source through
the protocol). Also landed en route: `BevelGeometry` `RoundCorners` (concentric
ring corner patches on a least-squares-fitted sphere instead of the single-point
fan), and a `TYPE_MISMATCH` guard in the protocol's `connect` handler (a
mismatched connection previously crashed the editor). Milestone verified
by protocol screenshot: beveled cube organically deformed by distance-to-ring-
points through the composed field chain; both test gates green.

Done: `CustomScalarField` — Roslyn snippet op (v1 of the VEX direction): the
Code input is a method body returning float with `p`, `A`-`D`, `Points`
(separator-free Vector3[] snapshot) in scope, `System.MathF` and the `FieldCode`
helpers (`DistanceToClosestPoint`) statically imported. Compiles into a
collectible AssemblyLoadContext (old one unloaded on recompile), errors are
logged with snippet-relative line numbers, and the previous working delegate
stays active while typing. Adds `Microsoft.CodeAnalysis.CSharp` to Lib — see the
packaging note below. Verified via protocol: sine-product snippet displaces the
beveled cube; bad code logs a warning and keeps rendering.

Packaging: the op carries `[ExportDependencies("Microsoft.CodeAnalysis.dll",
"Microsoft.CodeAnalysis.CSharp.dll")]`, so player exports only ship Roslyn when
an exported graph actually uses `CustomScalarField`.

Still open in this phase: the `SelectGeometry` Field mode (lands with
`SelectGeometry` itself in Phase 5). Renderer follow-up surfaced: making
`DrawMesh` multiply vertex `ColorRgb` (default 1 ⇒ visually safe, suite-guarded)
would activate `ColorFromField` and glTF vertex colors — maintainer decision.

- Core: named delegate types doubling as slot types — `ScalarField(in
  FieldSample) -> float`, `RemapCurve(float) -> float`, `VectorField(in
  FieldSample) -> Vector3`; `FieldSample` struct (position first, extendable);
  slot wrapper with the optional description-node field (empty in v1); type
  registration + colors.
- Producers:
  - `DistanceToPointsField` — `Point[]` -> `ScalarField` (distance to closest
    point). Builds a spatial grid once per input change; the closure captures it
    — showcases the state-capture guideline (param tweaks don't rebuild chains).
  - `GainAndBiasCurve` (`RemapCurve`), `RemapFieldValues` (`ScalarField` +
    `RemapCurve` -> `ScalarField`), `NoiseField`.
  - `CustomScalarField` — minimal code-string op (Roslyn): snippet + `Point[]`
    input, small context API incl. `DistanceToClosestPoint()`. The v1 of the
    VEX-like direction, with presets-for-code-parameters as on
    `CustomPointShader`.
- Consumers: `SelectGeometry` Field mode (evaluates a `ScalarField` at element
  positions -> selection weight — the formerly deferred "selection volume by
  field"), `ColorFromField` for direct visual feedback.
- Milestone: beveled cube colored / selection-weighted by
  `Points -> CustomScalarField{ DistanceToClosestPoint() } -> ScalarField`.

### Phase 3 — Chunks, parts & fracture demo 🔶

Done: `ScatterPointsInVolume` (deterministic PCG-seeded box/mesh-volume scatter,
ray-parity inside test, optional ScalarField density via rejection sampling),
`VoronoiFracture` (bisector-plane clipping per seed with Sutherland-Hodgman,
epsilon-chained cap loops — exact quantized keys broke on float rounding —,
interpolated corner normals so beveled surfaces stay smooth, flat caps with
face-domain Selection = 1, one part per cell with the seed as pivot), and
`ExplodeGeometry` (shrink parts toward pivots + push from center; the quick CPU
way to reveal parts until chunks land). Verified via protocol: beveled cube →
scatter → fracture → explode shows watertight chunks with smooth outsides and
flat caps.

Also done (user request): `LoadObjGeometry` — own N-gon-preserving OBJ parser
(quads stay quads for beveling; `ObjMesh` triangulates on load, so it wasn't
reused), `o`/`g` groups become parts with centroid pivots, normals/UVs become
corner attributes, file-watched via `Resource&lt;T&gt;`, `Scale` input.

Done (2026-09-03): `MeshChunkDef` promoted to `Core/Rendering` next to `PbrVertex`
(GPU-layout structs live there; `Core/DataTypes` is for connection types — layout
frozen, `LoadGltfScene` uses it). `Lib.Utils.GeometryMeshCompiler` now does the packing for
both `GeometryToMeshBuffers` and the new `GeometryToChunks`; because a part is a
contiguous face range and there is one vertex per corner, each part maps to
contiguous vertex/triangle ranges and the chunk table falls out without
reordering. `GeometryToChunks` outputs `Buffers` (with `ChunkDefsBuffer`),
`Points` (CPU pivots, seed index in F2), `GPoints` (uploaded), `ChunkIndices`
(identity) and `ChunkCount`; vertices are pivot-relative. Verified via protocol:
chunks render at the CPU mesh's position, `TransformPoints` scale 1.8 on the
pivots explodes the fracture on the GPU, and the CPU route (`Points` ->
`ListToBuffer` -> `TransformPoints`) gives the same picture. Noted on the way:
`ListToBuffer` fed straight into `DrawMeshChunksAtPoints` showed nothing once
right after its element count changed (1 -> 20) — likely a stale view in that
op after a buffer resize; not reproduced on retry, not in scope.

`PlaceGeometryAtPoints` (2026-09-03): prototype geometry x CPU point list -> one
part per point (position/orientation/scale from the point, corner normals
rotated with inverse-scale correction, point color as part-domain `Color`,
point index as `SourcePoint` part attribute and part seed index, separators
skipped, prototype parts flattened, other attributes repeated). Verified:
40 scattered beveled cubes -> 4560 faces / 3200 points / 40 parts, watertight,
volume = 40x the prototype's. Editor now also warns when a `.cs` under
`Symbols/` defines no operator (helpers belong in `Utils/`); the convention is in
AGENT_INSTRUCTIONS.

Still open: the full milestone demo (density field, animated pivots). A CPU `TransformCPoints` only moves a single point; the CPoints
family has no whole-list transform yet, so pivot animation currently goes through
the GPU point ops.

- Promote `MeshChunkDef` to Core.
- `GeometryToChunks` — one chunk per part into shared buffers; outputs
  `MeshBuffers` (with `ChunkDefsBuffer`) + `Point[]` pivots + chunk-index buffer
  -> existing `DrawMeshChunksAtPoints` unchanged.
- `PlaceGeometryAtPoints` — CPU instancing: prototype geometry + `Point[]` ->
  parts (the "PlaceInSlots" of the reverse-graph idea).
- `ScatterPointsInVolume` — CPU scatter in bounds/mesh volume, density driven by
  an optional `ScalarField` input, deterministic per seed.
- `VoronoiFracture` (pulled forward from deferred) — mesh + seed `Point[]` ->
  one part per cell; cells are convex, so plane-clipping + cap fill in managed
  C# — no native boolean kernel. Interior faces come out selected.
- Milestone (demo): `CubeGeometry -> BevelGeometry -> VoronoiFracture` with
  seeds from `ScatterPointsInVolume`, density from `DistanceToPointsField`
  ("cell size from distance to closest point") -> `GeometryToChunks` ->
  displaced pivots -> `DrawMeshChunksAtPoints` explode.

### Phase 4 — Curve foundation ⬜

- Core: `CurveGeometry`.
- De-static-cache `BezierPointSpline` (`_lengthList` is not thread-safe); reuse
  `Core/Utils/Splines/Bezier.cs` for evaluation.
- Ops (`Operators/Lib`):
  - `SvgToCurves` — SVG.NET path -> beziers, **no `Flatten()`**; later shrinks the
    duplicated GraphicsPath pipelines in `LoadSvg.cs` / `LineTextPoints.cs`.
  - `TextToCurves` — font outlines -> per-glyph closed contours; attributes: char,
    codepoint, glyph id, character/word/line index, advance, baseline, plus a font
    reference so downstream ops can re-instantiate outlines (variable fonts).
    Skip whitespace geometry, preserve advance.
  - `SetFontAxis` — variable-font axis (e.g. `wght`) per glyph part, scaled by the
    part selection weight; re-instantiates affected outlines. Layout toggle: frozen
    (default — animating axes doesn't reflow the line) vs re-layout with new advances.
    Enables e.g. `String -> TextToCurves -> SelectGeometry(Gradient) -> SetFontAxis
    -> CurvesToMesh` for a weight ramp across extruded text.
  - `CurveFromPoints` — through-points constructor (subsumes `SplinePoints` sampling).
  - `TransformCurves`, `ResampleCurves` (arc-length even), `CombineCurves`,
    `SetCurveAttribute`.
  - `CurvesToPoints` — late flatten, emits separators; the bridge keeping every
    existing point/draw op working. Explicit attribute mapping: which attributes
    land in `Point.Color` / `F1` / `F2` (default: color attribute -> Color).
- Milestone: SVG and text render via `CurvesToPoints -> DrawLines` with per-glyph colors.

### Phase 5 — Text slice ⬜

- New dependency: **LibTessDotNet** (managed, permissive) for constrained
  triangulation with holes/fill rules — `DelaunatorSharp` is unconstrained and can't
  fill concave glyphs.
- Ops:
  - `CurvesToMesh` — fill + depth + bevel size/subdivisions + front/back/side flags
    in **one op** (bevel needs boundary adjacency that separate Fill/Extrude ops would
    lose; depth 0 = flat fill). One part per input contour group (per glyph).
  - `CombineGeometry` (concatenates parts; naming matches
    `CombineMeshes`/`CombineSDF`), `SetGeometryAttribute`, `ColorFacesFromAttribute`
    (attribute -> palette/gradient; works on `CurveGeometry` and `MeshGeometry`,
    e.g. `characterIndex % palette.Count`, contour height -> gradient),
    `SelectGeometry` (see above).
- Milestone: `String -> TextToCurves -> CurvesToMesh -> ColorFromAttribute ->
  GeometryToMeshBuffers -> DrawMesh`, animated text, flat frame times; plus
  per-glyph chunks via `GeometryToChunks` (scale/color per character via points).

### Phase 6 — Spaces & grammar-lite (affine only) ⬜

Spaces are oriented boxes = `Point` (Scale = extents, F1/F2 = id/seed).

- `CreateSpace`, `SplitSpace` (multi-output along axis, absolute/relative sizes),
  `RepeatSpace`, `InsetSpace`, `SpaceToBoxGeometry`.
- `RandomSplitPoints` — weighted deterministic split of a point list into N outputs
  (useful beyond geometry: 90% normal roofs / 10% special).
- Simple CPU primitives as needed: `CylinderGeometry` (`CubeGeometry` exists from Phase 1).
- Milestone: small temple-like structure from spaces + `PlaceGeometryAtPoints`,
  stable under seed/param changes.

### Phase 7+ — Deferred (own plans when picked up) ⬜

- **Scene document** (maintainer discussion 2026-09-03, own plan:
  `Plan_SceneDocument.md`): a format-neutral `SceneDocument` as the result of
  loading glTF/FBX/USD (nodes, meshes as `MeshGeometry`, materials as data,
  skins, clips, cameras, lights); `SceneSetup` becomes the derived draw setup
  with node settings as a non-connectable editable parameter of
  `SceneToDrawSetup`; `PickGeometryFromScene` / `GeometryToScene` bridge to the
  geometry ops. Geometry-side helpers that fall out: `FilterGeoPartsByAttribute`,
  `MergeGeometry`, skin weights as point attributes emitted by
  `GeometryToMeshBuffers`. Design rule kept here: parts are shape only
  (disjoint face ranges with pivot/ids/attributes) — hierarchy, materials and
  names live on the scene layer and are joined back through `Node` / `Material`
  / `SourcePoint` part attributes.

- **Non-blocking evaluation for slow geometry ops** (maintainer request 2026-09):
  Release-mode fracture/bevel on real meshes stalls the UI frame. Sketch: an op
  opts into async evaluation — Update kicks the compute onto a worker with an
  immutable snapshot of its inputs, keeps returning the last finished result
  (plus an IStatusProvider "computing..." hint), and swaps in the new geometry
  when the worker lands. Immutable-by-convention geometry flow makes the
  snapshot cheap; the hard part is invalidation races (a newer param change
  canceling an in-flight job). For deterministic frames (visual tests, exports),
  follow the established async-op pattern from [PlayVideo] et al.:
  `if (context.Playback.IsRenderingToFile) Playback.OpNotReady |= !result.IsReady;`
  — the render loop then re-runs the frame until every async op has settled.
  **SHIPPED 2026-09 as `Core/Utils/AsyncComputation<T>`**: ops opt in via an
  `Async` bool input; the helper runs one job at a time on a worker, keeps
  publishing the last finished result, drives `DirtyFlagTrigger.Animated`
  while computing, and sets `Playback.OpNotReady` when rendering to file.
  Change detection via an `inputsVersion` hash of params +
  `MeshGeometry.Version` (bumped in `InvalidateTopologyCaches`, since geometry
  instances are reused). Async jobs build into a fresh `MeshGeometry`; the
  sync path calls `WaitForPending()` first so a toggle can't race the shared
  scratch buffers. Wired into `BevelGeometry` and `VoronoiFracture` (default
  off); roll out to further heavy ops as they land. Known v1 limit: a sync
  upstream op can mutate the source instance mid-read — the worker catches,
  logs, and retries on the next version change. Per-op optimization
  (spatial grids, parallel-for) stays deferred until profiles say where.
  Cancellation: when the inputs version changes while a job runs, the helper
  cancels it (compute gets a `CancellationToken`; ops call
  `ThrowIfCancellationRequested()` in their main loops) and starts the newer
  inputs as soon as the old worker has exited — "too slow, lower the
  resolution" no longer waits for the doomed job. `WaitForPending()` cancels
  too. Cancelled jobs are discarded silently; failures still log.
  Progress UI: workers call `AsyncComputation.ReportProgress(0..1)`; ops expose
  it via `IProgressProvider` (Core interface), and the MagGraph node renderer
  draws an orange bar at the node's bottom edge once a job runs longer than
  0.5 s (`TryGetUiProgress` gates the delay). Fracture reports per-seed (its
  cost is ~seeds x faces, so that's linear); bevel reports coarse stages.
- **Attribute-domain stress test (2026-09) — passed.** Use case: color the cut
  faces of each fracture chunk from a `ColorList`. Landed: face-domain `IsCut`
  on fracture caps (`Selection` mirrors it), `GeometryPart.Seed` renamed
  `SeedIndex`, face→corner and part→corner **Color promotion** in
  `GeometryToMeshBuffers`, and `ColorFacesFromAttribute` (index source: part index /
  part seed index / named face attribute; palette wrap repeat or clamp;
  OnlySelected masks by face Selection; preserves upstream corner colors).
  Correction to an earlier note: `mesh-Draw.hlsl` already multiplies
  `PbrVertex.ColorRGB` into albedo — no shader change was needed. Only
  `DrawMesh`/`DrawUnlit`/custom draw honor vertex color; `DrawWithShadows` and
  the instanced drawers ignore it (follow-up if needed). Edge-domain "WasCut"
  deliberately NOT stored: derivable from `IsCut` adjacency, and edge indices
  are lazily derived so stored edge attributes would risk going stale.
  Open architectural point: per-part data beyond `GeometryPart`'s fixed record
  should go into part-domain `GeometryAttributes` (length = Parts.Length) — the
  promotion path in the compile step already handles that domain.
- **Follow-ups on the stress test (2026-09)**: op renamed `ColorFacesFromAttribute`
  (it writes per-face colors); its `Attribute` input is an `ICustomDropdownHolder`
  listing "Part Index", "Part Seed Index" and every face attribute of the last
  evaluated input (`Usage: CustomDropdown` in the `.t3ui`). **Fracture speedups**:
  cells built in parallel (`Parallel.For` with a per-thread `CellBuilder` holding
  all scratch), exact plane culling (other seeds sorted by distance; stop once the
  bisector distance exceeds the cell's bounding radius), pooled polygons. Sync
  timings on the beveled cube (Debug build): 50 seeds 27 ms, 200 → 50 ms, 800 →
  117 ms, 2000 → 217 ms — near-linear where it was quadratic.
  **Round two on a real scan (ape.obj, 51k faces, seeds inside the volume)**: the
  first parallel version still took 37 s for 1600 seeds. Two fixes: (1) the cell
  is computed hull-first — the mesh bounding box (6 quads) is clipped by the
  culled planes, giving the effective plane list and a tight cell AABB; source
  polygons then come from a uniform grid over that AABB, skipping empty grid
  cells and grid cells/polygons outside any plane; fully-inside polygons are
  kept by reference, only straddling ones are cloned and clipped. Per the phase
  profile the fracture itself then costs ~70 ms at 400 seeds. (2) The dominant
  cost turned out to be `ScatterPointsInVolume`'s inside test — a ray-parity
  test against *all* triangles per candidate (~50 attempts per seed): now a 2D
  YZ grid over the fan-triangulated mesh, rebuilt only when `MeshGeometry.Version`
  changes. Whole-frame result (scatter + fracture + colorize + explode + upload,
  Debug): 60 seeds 200 ms, 400 → 356 ms, 1600 → 218 ms, 5000 → 517 ms; was 1.4 s /
  10 s / 37 s. Lesson recorded: profile the *chain* before optimizing one op.
  **Interior fragments** (maintainer spotted them missing): a cell no surface
  crosses produced no cut segments and therefore no caps - fully interior
  fragments silently vanished. Now the hull from pass 1 (bounding box clipped
  by the bisectors) is kept; if pass 2 yields no mesh polygons and the seed is
  inside the solid (`MeshInsideTester`, the YZ-grid ray-parity test shared with
  `ScatterPointsInVolume` via `Lib/Utils`), the hull is emitted as the fragment
  with every face marked IsCut. Seeds in empty space still yield nothing.
  Generalized right after (maintainer: "chunks outside the point cluster don't
  work"): caps were only ever chained from *surface* cut edges, so a plane that
  runs through solid interior without touching the surface (typical for the
  planes between seeds of a small cluster inside a big mesh) got no cap, and
  every later plane that would have chained along it broke too. Now every cap
  polygon carries its plane index; for a plane whose clip produced no cap, the
  corresponding hull face is used as the cap if its centroid is inside the
  solid. Fully interior fragments are just the case where that applies to all
  faces. Exposed as `FillInterior` (default on) because it assumes a closed
  solid - open or non-manifold scans can misfire on the inside test.
  **Status after the 2026-09 stabilization round (parked by maintainer decision
  - "keep this for later and move on")**: cap construction is now
  order-independent (clip by all planes first, then per plane: collect the
  surface edges lying in the plane, chain them, close open chains by walking
  the plane's exact hull face - a rectangle clipped by box and planes - and
  use the whole hull face when no surface crosses it and its centroid is
  inside). The inside test votes over three jittered rays (axis-aligned rays
  through shared triangle edges flipped the parity). Bugs fixed on the way:
  the walk skipped the originating chain so it could never close and swept
  every hull corner into the cap; pass 1 stopped early once the un-capped box
  hull emptied (dropped ~1/3 of the cell planes); bbox padding put hull edges
  0.001 off flat faces. Surface is now exact (emitted area == source area,
  silhouette matches at explode 0). **Remaining defect, measurable via the new
  `getOutput` MeshGeometry summary**: ~10 boundary edges per part (mostly
  cut-face edges, `boundaryEdgesOnCuts`), volume within ~1 % of the source -
  i.e. caps don't share edges exactly with neighbours (T-junctions / near-
  duplicate vertices along hull walks), which is also what shows as minor
  triangulation artifacts. Acceptance gate when resumed: `boundaryEdges == 0`
  and `|volume - sourceVolume| < 1e-4` on cube, beveled cube and the ape scan.
  Follow-up found via the boundary-edge samples: the per-cell point weld used
  plain rounding, so near-identical vertices straddling a rounding boundary
  (e.g. -0.04209502 vs -0.042094983) became separate points - the direct cause
  of open edges between caps and surface. Replaced by a tolerance weld that
  checks the neighbouring buckets - boundary edges dropped 1208 -> 777 at 120
  seeds, but not to zero, and the ~1 % volume error is unchanged, so a second
  cause remains (likely T-junctions where a cap's hull-walk edge meets several
  neighbour vertices). Parked here.
  **Resolved (2026-09-03), gate met**: 0 boundary edges, 0 non-manifold edges
  and `|volume - source| < 1e-4` across 13 configurations (12..1500 seeds,
  bevel 0..0.15, seed clusters 0.15..3.0 wide, plain cube), probed per part via
  `FilterGeoPartsByIndex` + `getOutput` (script pattern: `frac_probe.py` in the
  session scratchpad, plus an OBJ dump `getOutput dumpObj:<path>` analysed
  offline). Causes, in the order found:
  1. Every probe until then had fractured the *unbeveled* cube: `getOutput`
     bumped the stats tick instead of `GlobalInvalidationTick`, so its
     `InvalidateGraph` stopped at already-visited slots and consumers kept
     cached inputs; and `AsyncComputation` results landing inside an Update
     left no dirty signal for consumers that didn't pull in that exact frame
     (now: one-frame settle, trigger released by `WaitForPending(slot)`).
  2. The real cap defect: chain points held only segment starts, so an open
     chain's end vertex was never emitted - the cap jumped from the last start
     to the first hull corner, leaving one unmatched surface edge per hand-off.
  3. Degenerate Voronoi vertices (four cells meeting near a point) produce
     corner duplicates and slivers far above float precision; weld and chain
     tolerances are now relative to the mesh extent (weld 0.1 %, chain = weld,
     hull/merge = weld) and the weld buckets chain multiple points.
  4. A plane grazing the surface at a vertex has no usable cut segment; such
     faces (and any cut-less face bordering an emitted cap) are closed by an
     order-independent adjacency pass; edges on a plane coming from both
     adjacent surface polygons cancel (interior, not a cut).
  5. Two chains that failed to link (shared vertex computed twice, > chain
     tolerance) each walked the whole hull and built the same cap twice, which
     also made the divergence volume negative for that cell: chain starts
     within weld distance of the walk position are taken directly, and identical
     faces are dropped at emission.
  6. Safety net for what is left: boundary loops bounded entirely by cap edges
     (or tiny loops of any kind, < 8 x weld) are filled; loops touching surface
     edges stay open so open input meshes keep their border (camera-gizmo OBJ:
     8 boundary edges in, 16 out, volume unchanged).
- **Analysis helpers (maintainer request)**: `FilterGeoPartsInBox` keeps or
  discards parts by their pivot being inside a box (gizmo on Center/Size,
  `KeptCount` output; part-less geometry counts as one part; attributes
  remapped per domain) - for slicing a fracture open or culling chunks.
  `GetGeometryStats` outputs counts, bounds, size, signed volume, boundary
  edges, evaluation time of the upstream pull, and a text `Report`; stats are
  cached on `MeshGeometry.Version`. The debug protocol's `getOutput` now also
  accepts `outputName`.
- **Maintainer test findings (2026-09)**: `ExplodeGeometry` exploded from the
  mean part pivot, so filtering parts upstream shifted everything - now
  `AutoCenter` (default on) or an explicit `Center`. Fracture cap corners added
  by the hull walk carried a zero normal (black cut walls whenever the input
  had a Normal attribute) - now the plane normal. Fracture emission drops
  repeated consecutive corners (a duplicate made one fan triangle degenerate -
  "missing second triangle" on cut quads). `MeshGeometry` ops with Geometry as
  first input/output are now **bypassable** (`Symbol.Child._bypassableTypes` +
  `Instance.SetBypassFor` case) - Bevel, Transform, Displace, Triangulate,
  Fracture, ColorFaces, Explode, Center, Filter.
  Bug found on the way: `ColorFacesFromAttribute` indexed `Parts[0]` on part-less geometry (the
  unfractured input that flows while an async fracture computes) and the
  unhandled exception **killed the editor** — fixed (implicit part), but note the
  general fragility: an exception in any op's Update terminates the process; a
  try/catch-with-status at the slot evaluation level would be worth its cost.
- `CenterGeometry` (maintainer request): bounding-box centering with a
  normalized pivot — (0,-0.5,0) puts the bottom center on the origin; part
  pivots are translated along. For OBJ imports that aren't centered.
- Debug protocol `resetView`: reframes the output camera on the origin — the
  persisted view camera of a playground can point away from the origin, which
  made every screenshot empty and broke the acceptance test; the test fixture
  now calls it in `OpenPlayground`.
- **Bypass toggles were flaky** (maintainer: "doesn't always have an effect",
  looked like fracture caching). Root cause in Core: `DirtyFlag.Invalidate()`
  de-duplicates per invalidation tick, and the tick advances in the output
  window's draw — a bypass toggled from the graph/parameter window (drawn after
  it) hit an already-visited slot and the invalidation was dropped until some
  unrelated change dirtied the op. Pure pull-cached ops (geometry) made this
  visible; command/texture chains re-evaluate anyway. Bypass/restore and
  `InvalidateConnected` now use `ForceInvalidate()`.
- `FilterGeometryParts` renamed `FilterGeoPartsInBox` (guid kept); new
  `FilterGeoPartsByIndex` (`Start`/`Count`, negative Start from the end, Count 0
  = rest, `PartCount` output). Both share `Lib.Utils.GeometryPartSubset`
  (face/point compaction + per-domain attribute remap). Isolating one fracture
  chunk immediately shows the parked cap defect per part (chunk 0 of a 30-seed
  cube: 13 faces, 16 boundary edges) — the intended handle for finishing it.
- **Geometry output view** (maintainer request, 2026-09-03): selecting a
  geometry op now shows `MeshGeometryOutputUi` (headline, counts, size, bounds,
  volume, surface status, attribute table) instead of an empty view. The
  measurement moved to Core as `MeshGeometryStats` (shared with
  `GetGeometryStats`; remeasures only on `Version` change, strings rebuilt only
  then). A per-part table (faces, open edges, volume, seed, pivot; clipped
  rows, strings cached per change) followed; `MeshGeometryStats.Parts` carries
  the per-part numbers. Hover-driven views and a wireframe preview were
  considered and dropped.
- `ExtrudeFaces`, bevel v2 (miters, colliding bevels, per-edge widths),
  `SubdivideGeometry`, `LatticeDeform`, `SliceGeometryWithPlane` (-> closed
  `CurveGeometry`), curve offsetting, `FitCurves` (polyline -> smooth beziers).
- `DecimateGeometry` — selection-weighted ratio; fast vertex-clustering mode +
  quality edge-collapse mode; quantize animated selections so tiny changes don't
  retrigger. Enables pose-driven spatial decimation (joint point -> ByPoints
  selection -> decimate).
- GPU->CPU bridge ops: `MeshToGeometry` (MeshBuffers readback),
  `TextureToContourCurves` (texture readback + marching squares, height as contour
  attribute — the contour-lines flow). Both dirty-cached.
- CPU primitives (`TorusGeometry`, ...): consider refactoring existing CPU generators
  (`TorusMesh` et al. already build `PbrVertex[]` in C#) to emit `MeshGeometry` and
  compile — one code path instead of two.
- CGA-inspired `ShapeGrammar` script op (lexer/AST/evaluator -> parts; shader-graph
  compile pattern as precedent). Build when graph-native ops start hurting.
- **`CustomGeometryCode` — full attribute-aware per-element kernel op**: the
  grown-up version of Phase 2's `CustomScalarField` (which handles spatial
  scalar snippets only). Adds element index, attribute accessors by
  `AttributeId`, richer spatial queries, topology-aware writes — "offset
  attribute weight by distance to nearest surface, remap with GainAndBias, add
  jitter" as five lines instead of five ops. Ops for structure, snippets for
  per-element math.
- **Dual-backend field ops** (same op emits HLSL for GPU consumers and C# for
  CPU consumers — unified field vocabulary vs double authoring cost or a
  backend-neutral IR): open fork, kept reachable via the Phase 2 slot wrapper's
  optional description-node field (a bare Func is forever opaque; the node makes
  translation/fusion possible later).
- Non-affine space mappings (bilinear quad / dome / warped) — changes the space
  contract from matrix to mapping; only when a concrete case (dome coffers) forces it.
- Native kernel package (booleans/remesh/decimate via coarse-grained bridge) — in its
  own operator package, never coupled to Lib's hot-reload path.

### Wrap-up ⬜

- `.help/` pages (text workflow, geometry pipeline overview).
- `.tests-manual/` set (text slice end-to-end; chunk coloring).
- Sweep transitional comments; feature retrospective.

## Compatibility commitments (no breaking changes by design)

- Every op is new; no existing operator changes Guid, parameters, or output behavior.
  `LoadSvg` / `LineTextPoints` / `SplinePoints` are superseded, not modified.
- **Vertex order is de-facto API**: existing generators (`CubeMesh`, ...) keep their
  exact emitted layout; retrofits onto the geometry path only with bit-identical output.
- Struct layouts frozen: `Point` (64 B), `PbrVertex` (80 B), `MeshChunkDef` (16 B) —
  all baked into shaders.
- Separator convention: unified once in Phase 0 (legacy `W`/`FX1` checks removed
  everywhere, `Scale.x`-NaN is the only convention). Shaders and emitters ship
  together in Lib, so the sweep is internally consistent; user-authored custom
  shaders relying on the `W` check are considered negligible.
- Promoting `MeshChunkDef` out of `LoadGltfScene` into Core is a source-level change
  but not critical — very few (if any) third-party packages reference the nested
  type. Move it cleanly, one release-note line. Binary layout unchanged.
- No new operator package (all ops in Lib, pure managed) -> no `Editor.csproj`
  `<PackageNames>` Release-build concern until the future native kernel package.

## Validation scenarios (thought experiments the architecture must support)

Worked through 2026-09-02; each maps onto planned ops without new concepts:

1. **Pose-driven decimation** ("hand reaches into polygon optimizer"):
   `SampleGltfAnimation -> pose joint point -> SelectGeometry(ByPoints, falloff)
   -> DecimateGeometry -> compile`. Exercises ByPoints selection, attribute
   interpolation through collapse, per-frame re-evaluation cost.
2. **Contour lines**: `FractalTexture -> TextureToContourCurves ->
   ColorFromAttribute(height -> gradient) -> ResampleCurves(even) -> CurvesToPoints
   -> DrawLines`. Exercises readback bridge, curve attributes, flatten mapping.
3. **Torus fracture + explode**: `TorusGeometry + seed points -> VoronoiFracture ->
   GeometryToChunks -> displace pivot points -> DrawMeshChunksAtPoints`. Exercises
   parts->chunks, seeds-as-points control, per-frame animation without touching
   geometry.

Recurring pattern: `Point[]` is the universal glue (pose -> selection, seeds ->
fracture, pivots -> explode); GPU<->CPU crossings stay explicit and cached.

## Open questions

- `Core/DataTypes` audit (maintainer, 2026-09-03): the folder/namespace is meant for
  connection types, but holds buffer-element and helper structs too (`Point`,
  `EmitterCounter`, `Sprite`, `LegacyParticleSystem`, `Shader`). Moving them is a
  wide mechanical refactor (external operator packages reference
  `T3.Core.DataTypes.Point`); do it as its own commit.

1. **Font outline library** — decided: **SixLabors.Fonts**. `IGlyphRenderer`
   streams glyph outlines as beziers while `TextRenderer` drives full layout
   (kerning, ligatures, multi-line, spacing); covers TTF/OTF-CFF/WOFF2; pure managed.
   Split License resolves to Apache 2.0 for OSI-licensed consumers (TiXL is MIT) and
   for transitive downstream use; additionally, SixLabors confirmed directly to the
   maintainer (2026-09) that use in TiXL is free and permitted. Fallback if needed:
   Typography.OpenFont (MIT, similar streaming API, swap contained in `TextToCurves`).
2. **Part table shape**: contiguous-face-ranges-per-part assumed (makes chunk emission
   trivial); verify it survives `CombineGeometry` + selection-based edits without
   frequent re-sorting.
3. **`SelectGeometry` granularity**: one op with Domain/Mode enums vs a small family
   of concrete ops. Start generic; split if parameters sprawl.
4. **Chunk metadata**: does picking need per-chunk bounds in the GPU struct, or can it
   stay on the CPU part table?
5. **Variable-font axis instantiation in SixLabors.Fonts** — spike early in Phase 1:
   verify arbitrary axis values are applied to outlines (fvar/avar/gvar, CFF2).
   Fallback if incomplete: instantiate two static extremes (e.g. wght 100/900) and
   lerp control points per glyph — variation-compatible masters share point structure,
   so this approximates gvar linearly and `SetFontAxis` becomes a cached lerp.

## Reference files

- `Core/DataTypes/MeshBuffers.cs`, `Core/Rendering/PbrVertex.cs` — GPU-side target format
- `Operators/Lib/Symbols/render/scene/LoadGltfScene.cs` — `MeshChunkDef`, chunk build path
- `Operators/Lib/Symbols/render/shading/_/_GetSceneDefinitionPoints.cs` — point+chunk-index pairing
- `Operators/Lib/Symbols/mesh/draw/DrawMeshChunksAtPoints` + `Assets/shaders/3d/mesh/chunks/`
- `Core/DataTypes/Point.cs`, `Core/DataTypes/StructuredList.cs` — point/slot currency
- `Core/Utils/Splines/` — bezier + arc-length sampling to reuse
- `Operators/Lib/Symbols/point/io/LoadSvg.cs`, `LineTextPoints.cs` — import paths to converge
- `Core/DataTypes/ShaderGraph/` — compile-a-subtree precedent for the later script op
- `Plan_GltfAnimation.md` — reuse-existing-types precedent (`Point[]` poses)
