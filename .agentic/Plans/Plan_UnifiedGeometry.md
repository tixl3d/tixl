# Plan: Unified Geometry (points, vertices, primitives)

Status: **Decided, not started.** Decisions below. Written 2026-11-20. The maintainer and team chose one CPU geometry type, as in
Houdini, over separate curve and mesh types. Supersedes the data-type parts of `Plan_ProceduralGeometry.md` (which
stays the reference for ops, fields, parts, chunks and the fracture work) and adjusts `Plan_ShapeEditing.md`.

## Goal

One wire type, `Geometry`, that holds points plus primitives of different kinds: polygons and curves. Point,
selection, transform and attribute ops are written once and work on everything. Going from 2D to 3D stays an
operation (draw curves, then extrude them), as in Fusion and Plasticity, not a change of wire type.

## Why

- **Paired ops.** With `CurveGeometry` and `MeshGeometry`, every point-level op needs two versions
  (`TransformCurves` and `TransformGeometry`, a select op per type, ...). That, not the number of point types, is
  what made the model confusing.
- **The zigzag test.** `StringToCurves -> SubdivideCurves -> SelectGeometry (every 2nd point) -> TransformGeometry
  (Space: Point)` should just work, and its result should still extrude. With CPoints it needs a lossy round trip
  through separator points; with two geometry types it needs curve versions of select and transform.
- **Mixed content.** Text outlines and the body extruded from them can flow through one connection, as a Fusion
  sketch sits beside its body. Selections survive the step from curves to polygons.
- **Shared vocabulary.** Houdini's terms are what many users already know, and what tutorials use.
- **Cheap now.** `MeshGeometry` already is points, per-face corner lists and attributes, i.e. points, primitives and
  vertices. Curves have 5 ops and 1 helper (`StringToCurves`, `OffsetCurves`, `SubdivideCurves`, `CurvesToPoints`,
  `CurvesToGeometry`, `CurveWinding`). The help page marks procedural geometry as experimental, wire types included.

## Data model

```csharp
public sealed class Geometry
{
    public Vector3[] Positions;              // Point domain
    public int[] PrimitiveVertexOffsets;     // CSR, PrimitiveCount + 1 entries
    public int[] VertexPoints;               // Vertex domain: the point each vertex references
    public PrimitiveKinds[] PrimitiveKinds;  // Polygon or Curve
    public bool[] PrimitiveClosed;           // curves only; polygons are always closed
    public GeometryPart[] Parts;             // contiguous primitive ranges
    public GeometryAttributes Attributes { get; }
    public EdgeTopology Edges { get; }       // derived on first access, polygon edges
    public int Version { get; }
}
```

**Domains** (`AttributeDomain`): `Point`, `Vertex`, `Primitive`, `Edge`, `Part`. A later `Detail` domain (one value
for the whole geometry) is optional.

| Today | Unified |
| --- | --- |
| `MeshGeometry` face | Polygon primitive |
| Corner | Vertex |
| `CurveGeometry` contour | Curve primitive with a closed flag |
| Anchor (ControlPoint) | Point, referenced by one vertex of the curve |
| `HandlesIn` / `HandlesOut` | Point attributes `HandleIn` / `HandleOut`, offsets from the point |
| Segment attributes | Vertex attributes (each vertex of a curve starts a segment) |
| `CurvePart` / `GeometryPart` | `GeometryPart` over primitive ranges |
| Point cloud (mesh without faces) | Points without primitives |

**One primitive list with a kind per primitive.** Polygons and curves are both ordered vertex lists over shared
points; they differ only in the closed flag, handles and filling, which the kind, `PrimitiveClosed` and the handle
attributes cover. Considered and rejected:
- *Primitive objects* (`FacePrimitive`, `CurvePrimitive` classes): readable, but 100k+ objects for a fractured mesh,
  GC pressure for animated geometry, and per-primitive attributes still need dense arrays by index. Houdini's
  primitive classes are also wrappers over flat storage.
- *Two lists in one geometry* (faces and curves over shared points): no kind checks and today's code stays as is, but
  two primitive domains (two selections, two color attributes), parts can't span an outline and its extrusion, and
  generic ops handle both lists.

Decided 2026-11-20: one list with typed views.

Ops get typed, allocation-free views instead: `geometry.Polygons` and `geometry.Curves` enumerate structs with
`VertexCount`, `GetPoint(i)`, `IsClosed` and, for curves, `Evaluate(t)`, so most code never reads the kind array.

**Curves** stay cubic bezier, flattened late. A polyline is a curve whose handles are zero.

**Handles are point attributes, not points.** Houdini makes bezier handles points of the hull, so transforms move
them for free, but then "every 2nd point" includes handles. Here points are anchors, which is what users select.
The cost is that ops moving points must transform handles too, which the next rule solves generally.

**Attributes carry type info** (`Value`, `Vector`, `Normal`, `Quaternion`, `Color`, `Position`), as Houdini's type
info does. Transform and deform ops transform every attribute by its type info instead of special-casing names:
`[TransformGeometry]` today recognises `Normal` by name. Handles are `Vector`, normals `Normal`, `Orientation`
`Quaternion`.

**String attributes** for names (markers, layers), never packed for the GPU, sharing the `StringColumn` planned in
`Plan_StructuredData.md`. `GeometryAttribute<T>` stays `unmanaged`; strings get their own attribute class.

## Points: two worlds and a bridge

Replaces the "retire CPoints" section of `Plan_ProceduralGeometry.md`.

- **GPU points** (`BufferWithViews`): realtime work and the large op library. Unchanged.
- **Geometry points**: the Point domain of a `Geometry`, with or without primitives. Not a type of its own; point ops
  act on the points of whatever geometry they get. Point attributes use the `Point` field names (`Orientation`,
  `Scale`, `Color`, `F1`, `F2`) plus `Id` and `Seed`, so conversion is lossless and point ops take the same parameters
  as their GPU counterparts (Space: Object/Point, Strength, selection).
- **CPoints** (`StructuredList<Point>`): the bridge. GPU readback for locators, lights, cameras and skinning stays.
  CPoint generators get no new work and are tagged obsolete once geometry replacements exist; many are a curve's
  samples (`CircleCurves -> ResampleCurves`).
- **Crossing over:** `GeometryToPointBuffer` uploads points to the GPU with an attribute mapping;
  `PointsToGeometry` turns CPoints into geometry points. `CurvesToPoints` keeps flattening curve primitives into
  separator-delimited line points for `[DrawLines]`.

## Ops

| Today | Unified | Notes |
| --- | --- | --- |
| `StringToCurves` | same name | Outputs curve primitives |
| `SubdivideCurves`, `OffsetCurves` | same names | Act on curve primitives, pass polygons through |
| `CurvesToGeometry` | `ExtrudeCurves` | Fill, extrude, bevel; option to keep the source curves. The GPU op of that name becomes `ExtrudePointCurves` |
| `CurvesToPoints` | same name | Projection of curve primitives for line drawing |
| Mesh ops (`BevelGeometry`, `VoronoiFracture`, ...) | same names | Face -> Primitive domain; skip curves, report "no polygons in input" via `IStatusProvider` |
| `GeometryToMesh`, `GeometryToMeshChunks` | same names | Polygons only; curves ignored |
| `TransformGeometry` | same name | Attribute type info; gains Selection and Space (Object/Point) |
| `ScatterPointsInVolume` | same name | Outputs geometry points |
| `PlaceGeometryAtPoints`, `VoronoiFracture` seeds, `DistanceToPointsField`, `CustomScalarField` | same names | Take geometry points; connections from CPoints break (see Migration) |
| new | `SelectGeometry` | Domain, modes from `Plan_ProceduralGeometry.md`, plus Custom (C# snippet, as `[CustomScalarField]`) |
| new | `DeleteGeometry`, `StoreSelection` | From `Plan_ShapeEditing.md` |

Polygon-only and curve-only ops check kinds at run time. Type safety moves from connecting to evaluating; the status
message must name what was expected.

## Editor

- One `GeometryOutputUi` replaces the curve and mesh views: Render, Table, Stats.
- **Render** draws polygons like `GeometryToMesh -> VisualizeMesh` and curves like `CurvesToPoints -> DrawLines`, in
  one preview composite.
- **Table** domains: Points, Vertices, Primitives (with a Kind column), Edges, Parts. The table model, view, selection
  and overlay built for curves and meshes merge into one.
- `DebugServer.getOutput` reports primitive counts per kind.
- The wire keeps the CPU geometry color.

## Migration

- **Connections survive the type change.** Saved graphs store connections by slot id, so ops that switch from
  `MeshGeometry` or `CurveGeometry` to `Geometry` keep their wiring. No operator versioning for that part.
- **CPoint inputs that become geometry inputs break connections.** Those ops go through
  `Plan_OperatorVersioning.md`, or keep a second, CPoint input for one release.
- `MeshGeometry` and `CurveGeometry` are removed once nothing uses them: no long-lived compatibility shims.
- Core touch points: type registration (`SymbolPackage.TypeRegistration.cs`), bypass (`Instance.Connections.cs`,
  `Symbol.Child.cs`), `MeshGeometryStats`.

## Phases

### Phase 1: Zigzag spike
- Renames first, as their own commit: the two namespaces and `[ExtrudePointCurves]` (done 2026-11-20, together with
  its example `ExtrudePointCurvesExample`, which links to the op by name).
- `Geometry` with polygon and curve primitives, typed views, attribute type info, Houdini domains (done
  2026-11-20: `Core/DataTypes/Geometry/Geometry.cs`. Views are `PolygonView` and `CurveView`, since
  `T3.Core.DataTypes.Curve` is the animation curve; the per-primitive arrays are `PrimitiveKind` and
  `PrimitiveClosed`; parts are `PrimitivePart`. `AttributeDomain` gains `Vertex` and `Primitive` at the end so stored
  ints stay valid; type info defaults from well-known names. Registered as wire type, bypassable, CPU geometry
  color. No ops use it yet.)
- `StringToCurves` and `SubdivideCurves` converted (2026-11-20). Subdivide renumbers curve points along each curve
  and carries point and vertex attributes through `Lib/Utils/GeometryAttributeBlend`; polygons pass through.
- `SelectGeometry` (new, `geometry/attribute`: Index Pattern and Custom C# snippet returning bool or float; Replace,
  Add, Remove, Intersect; Invert; named float attribute) and `TransformGeometry` (Space Object/Point via
  `Lib/Utils/GeometryPointOrientations`, named Selection promoted from vertices, primitives or parts, attributes transformed
  by type info) converted (2026-11-20). Snippet compiling is shared with `CustomScalarField` in
  `Lib/Utils/CSharpSnippetCompiler`. Verified: the zigzag chain moves every second point 0.03 across the outline.
- `CurvesToGeometry` renamed to `ExtrudeCurves` (symbol id kept, `AKA: CurvesToGeometry`) and converted, with
  KeepCurves; `GeometryToMesh` compiles `Geometry` polygons through the shared `GeometryMeshCompiler`, which now
  writes compacted vertices for both types (2026-11-20). Verified: the extruded zigzag text is closed (0 boundary
  edges) and renders through `DrawMesh`.
- **Spike compromise:** `GeometryToMesh` keeps its original input id with type `MeshGeometry` (renamed `Mesh`) and gains
  a `Geometry` input, because `[SurfaceGeometry]` (Output Setup's New 3D Content), the Skills tutorial R04b and the
  mesh preview feed it `MeshGeometry`. Phase 2 merges them back into one input once those sources are converted.
  (Selection, Space), `GeometryToMesh`; add `SelectGeometry` with Index Pattern and Custom modes.
- Merged output view.
- Milestone: zigzag text, extruded, in the editor. **Decision point**: continue, or stop and keep the two types.

### Phase 2: Migrate everything
- **Flat fill without extrusion** (maintainer, 2026-11-20): curves must become a surface without an op named
  "extrude". Move the fill (flatten, resolve overlaps by fill rule, tessellate with holes) out of `ExtrudeCurves` into
  a `Lib/Utils` helper, then (1) `GeometryToMesh` fills closed curves itself (option, on by default) so
  `StringToCurves -> GeometryToMesh` renders flat shapes, and (2) a `FillCurves` op outputs the flat polygons as
  Geometry for further modelling (Houdini's PolyFill). `ExtrudeCurves` keeps walls and bevels.
- The remaining mesh ops (mechanical: Face -> Primitive, Corner -> Vertex), `OffsetCurves`, `CurvesToPoints`, output
  setup's `StageGeometry` and `SurfaceGeometry`.
- Remove `MeshGeometry`, `CurveGeometry`, `CurvePart`.
- One commit for the mechanical rename, separate from behavior changes.

### Phase 3: Points bridge
- Geometry points in `ScatterPointsInVolume`, `PlaceGeometryAtPoints` and the field ops, `GeometryToPointBuffer`,
  `PointsToGeometry`, string attributes, obsolete tags on CPoint generators.

### Phase 4: Docs and tests
- `.help/docs/using/ProceduralGeometry.md` with the Houdini terms, a geometry test set, release notes.
- Interface-stability audit of `Geometry`, the domains and attribute type info before the first stable release.

## Decisions

1. **One primitive list with a kind per primitive, plus typed views** (`Polygons`, `Curves`) (2026-11-20).
2. **`Geometry` is a main type and owns the name** (2026-11-20). Namespaces that clash are renamed:
   - `T3.Core.Utils.Geometry` (`Ray`, `GraphicsMath`, `PlaneExtensions`, `MatrixExtensions`, `VectorT3`; 46 files in
     the repo) -> `T3.Core.Utils.Math3d`. External operator packages that import it need a one-line change (one file
     in the maintainer's projects); mention it in the release notes.
   - `T3.Editor.Gui.OutputUi.Geometry` -> `T3.Editor.Gui.OutputUi.GeometryViews`.
3. **Ops that merely sound like geometry ops give up their names** (2026-11-20). The GPU `[ExtrudeCurves]` (rail and
   profile point buffers) becomes `[ExtrudePointCurves]`; graphs bind ops by symbol id, so its six uses keep working.
   Apply the same rule to any later collision: the `Geometry` family gets the plain names.

## Comparison with TouchDesigner POPs

POPs (TouchDesigner 2025) use the same Houdini model, a point list, a vertex list and a primitive list, each with
any number of typed attributes, but run entirely on the GPU as compute shaders.

| | POPs | This plan |
| --- | --- | --- |
| Where | GPU only; CPU SOPs remain the legacy path | CPU `Geometry`; GPU points stay a separate world |
| Primitives | points, lines, line strips (open or closed), triangles, quads | N-gon polygons, cubic bezier curves |
| Attributes | typed (int, uint, double), only changed ones are created | typed with type info; unchanged buffers shared by reference |
| Structure metadata | "dimensions" (points as rows and columns) | none yet |
| GPU/CPU crossings | avoided | explicit ops |

TiXL's choice fits authoring: exact text outlines, beveling N-gons and editing anchors need curves and N-gons that
GPU-friendly fixed-size primitives don't keep. POPs fit large per-frame processing, which TiXL's GPU points already
cover, without an attribute model. Two consequences:

- **Keep `Geometry` columnar** (one array per attribute, CSR topology) so an attributed GPU geometry, if TiXL ever
  wants one, could mirror the schema and upload attribute by attribute.
- **Dimensions** are worth copying later as `Detail`-level metadata for grids, so neighbor ops don't have to rebuild
  the structure.

## Open questions

1. **Curves in `GeometryToMesh`.** Ignore them (recommended for now) or emit them as lines?
2. **Detail domain** now or when a use appears?

## Risks

- **Big rename** across ~40 files. Keep it mechanical and separate; the spike decides before it starts.
- **Runtime type errors** replace connection-time ones. Clear status messages are part of each op's definition of done.
- **Performance** of polygon ops checking kinds: one array read per primitive, negligible; measure in the spike.
- **Handle transforms** forgotten by new deform ops. Attribute type info makes the right thing the default; a test
  that transforms a curve and compares with a reference keeps it honest.
