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
- **No `GeometryScene`.** `SceneSetup` already has the node hierarchy, per-node
  transform/material/visibility, and dispatch flattening. If a scene-level authoring
  step is needed later, it evolves `SceneSetup`.
- **Chunks**: promote `MeshChunkDef` from `LoadGltfScene.cs` into Core (next to
  `MeshBuffers`) instead of adding a parallel chunk type. Keep the GPU struct lean
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
- Modify ops (`ExtrudeFaces`, `TransformGeometry`, `ColorFromAttribute`, ...) take an
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
drops the Normal attribute since displacement invalidates it). Milestone verified
by protocol screenshot: beveled cube organically deformed by distance-to-ring-
points through the composed field chain; both test gates green.

Still open in this phase: `CustomScalarField` (Roslyn snippet op — the v1 of the
VEX direction), `NoiseField`, and the `SelectGeometry` Field mode (lands with
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

### Phase 3 — Chunks, parts & fracture demo ⬜

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
    `CombineMeshes`/`CombineSDF`), `SetGeometryAttribute`, `ColorFromAttribute`
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
