# Plan: Shape Editing (Illustrator-like curves and points in the Output window)

Status: **Design only, nothing implemented.** Written 2026-11-20. Builds on `Plan_ProceduralGeometry.md` (data types,
ops), `Plan_CameraOverhaul.md` (orthographic viewer camera) and the Output Setup canvas editing
(`output-setup/canvas-interaction.md`, `output-setup/selection.md`).

> **2026-11-20:** data types follow `Plan_UnifiedGeometry.md`. Read `CurveGeometry` as `Geometry` with curve
> primitives, anchors as points, ControlPoint/Contour/Segment as Point/Primitive/Vertex, and `MeshGeometry` as
> `Geometry` with polygon primitives. `[EditCurves]` edits the curve primitives of its input and passes polygons
> through.

## Goal

Draw and edit points, curves and shapes directly in the Output window, on planes viewed by an orthographic camera,
and feed them into the procedural geometry ops. Editing should feel like Illustrator: pen tool, anchors with bezier
handles, marquee, snapping, typed values. The node graph stays the history: what can be made with ops is made with
ops, and only what has to be drawn by hand is drawn.

Two workflows define "done":

**A: Lights on a foot path**
1. Screenshot a satellite image of a path along a river.
2. Put it on a plane in the scene, set its scale with a scale line, lock it and fade it.
3. Trace the path with the pen tool (top view).
4. Resample into points every 5 m, with a start offset to align the first lamp.
5. Switch to the side view and adjust the height of individual curve points.
6. Sweep a profile along the path, split it into planks, shrink and bevel them.
7. Instance lamp posts and light emitters at the points. Each emitter carries U along the path.
8. Color the lights from a gradient sampled by U.

**B: Temple**
1. Top view: draw a rectangle, fill and extrude it as the base.
2. Offset the rectangle inward, distribute points on it, instance columns on them.
3. Remove two columns for the entrance.
4. Front view: draw the roof polygon, extrude it.
5. Offset it inward for the tympanon. The selection is kept; extrude the inner face inward.
6. Convert to mesh.

## Scope: where to stop

| In scope | Out of scope (and what replaces it) |
| --- | --- |
| Direct editing of **curves and points**: anchors, handles, segments, contours, shapes | Per-vertex **mesh** editing. Meshes always come from ops; their parts are chosen with selections |
| Construction planes: world planes, then planes from points, anchors and faces | A parametric **constraint solver** (Fusion sketch constraints). Snapping and typed values instead |
| Overrides on procedural results (move, delete, set attribute), keyed by element index | A second **history tree**. The graph is the history |
| Authored attributes on curve elements | NURBS, surface modelling, mesh booleans |
| Reference images on planes, with scale, lock and fade | Animated shapes. Later, as shape keys blended by an op, not keyframes per anchor |

2D curve booleans (union, subtract, intersect) are the most likely later addition; they fit the Illustrator model and
LibTessDotNet already resolves fill rules.

## What exists

**Data.** `CurveGeometry` (cubic bezier anchors with absolute handles, contours, parts, attributes on ControlPoint,
Segment, Contour and Part) and `MeshGeometry` (N-gons, parts, attributes). Selections are float attributes. Neither type
can be stored in a `.t3`: `SymbolPackage.TypeRegistration.cs` registers them without a serializer, by design.

**Ops** (`Operators/Lib/Symbols/geometry/`): `StringToCurves`, `OffsetCurves`, `SubdivideCurves`, `CurvesToGeometry`
(fill, extrude, bevel), `CurvesToPoints`, `BevelGeometry`, `ExplodeGeometry`, `PlaceGeometryAtPoints`, `VoronoiFracture`,
`TransformGeometry`, filters by part index or box, `GeometryToMesh`.

**Canvas editing** (`Editor/Gui/Interaction/CanvasEditing/`): `ICanvasProjection`, `CanvasPointHandle` (drag phases,
snapping), `CanvasItemPicker` (hit stack, click cycling; its doc already names "points and lines of geometry
operators"), `CanvasDraw`, `CornerPinHandles`. `SelectionSet<T>` and `SelectionFence` in `UiModel/Selection/`. Used only by
the Output Setup Board today.

**Board features worth lifting** (all private to the `SetupOutputView` partial class today): gesture undo
(`BeginGesture` / mutate live / `EndGesture`), `PressHandoff` (click vs drag), marquee with Replace/Add/Remove,
`RectSnapping`, the polyline tool (`SetupOutputView.PlanDraw.cs`: click to plant, close on first vertex, 45 degree and
typed-length snapping), `MetricGridRaster`, and `ReferenceImage` (crop, `IsLocked`, `Opacity`, scale line setting
`MetersPerPixel`).

**Lessons from [Sketch].** `_SketchImpl` handles mouse, keyboard, lasso and overlays inside the op (2241 lines), reads
the mouse through `[MouseInput]`, and stores strokes in a sidecar JSON file named after the child id. It works, but:
editing code in an op is reloaded with the package, can't use editor widgets or themes, and the sidecar file isn't
copied with copy/paste or duplicated with the symbol. Its undo path, `OperatorUndoRedo`, is the right hook for op-owned
data. Shape editing puts the interaction in the editor and the data in the `.t3`.

**Missing.** Picking geometry in the 3D view (none exists; `TransformGizmoModes.Select` has no handler), an orthographic
viewer camera, curve generators other than text, and the ops listed under [Op gaps](#op-gaps).

## Design

### 1. Two kinds of editable data, one op (decided)

Both workflows edit **procedural** results: point heights after resampling (A5), two columns out of a distributed set
(B3). So authoring alone isn't enough.

`[EditCurves]` has an optional `CurveGeometry` input and one `ShapeDocument` parameter:

- **Authored elements:** contours drawn in this op, with their anchors, handles, closed flags and attributes.
- **Overrides:** changes to elements of the input, keyed by element index: move (anchor and handles), delete, set
  attribute. Overrides beyond the input's element count are kept and reported through `IStatusProvider`, never
  silently dropped, so changing an upstream parameter and back restores them.

Without an input the op is a pure drawing op. One op instead of `DrawShapes` + `EditCurves` means a drawn shape can
later be fed a procedural input without changing ops, and the editor handles a single parameter type.

A **Bake** command replaces an op chain by an `[EditCurves]` holding its current result as authored elements, for when
the procedural part is finished.

Point sets that need editing (B3) stay in the curve domain: `ResampleCurves` produces a curve whose anchors are the
points, edited by `[EditCurves]`, and `CurvesToPoints` converts late. This follows the geometry plan's rule that
`Point[]` is a projection format.

### 2. Element references: indices, robustness from flowing selections

Edits and component selections refer to elements by **index** within the editing op's input, per domain, as Maya's
component lists (`f[3]`) and Houdini's edit and group nodes do. Upstream changes that alter the topology shift what an
edit hits. That is the accepted contract of construction history, and users of those tools expect it.

Stable ids were considered and dropped:
- They can't be made to work in general. Points on a circle with some deleted have no identity beyond their index;
  hashing sources only moves the problem.
- They tax every op: each one would have to propagate or derive ids correctly, forever.
- An index is still a stored reference, to an element of one op's output (`SymbolChildId`, domain, index). It isn't
  the value matching that AGENT_INSTRUCTIONS warns about.

Robustness comes from two other places instead:
- **Selections flow as attributes.** An op that creates elements selects them, so the next op works on them without
  any reference (`Plan_ProceduralGeometry.md`, "Selections are attributes"). See [Selection through a chain](#selection-through-a-chain).
- **Procedural selectors** for stable picks: select by box, by drawn curve, by normal or by attribute instead of by
  clicking elements. Users who need a deletion to survive upstream changes use one of these.

**Cheap safety net:** the document stores each domain's element count when an override is made. When the input's
count differs, the op reports "input topology changed (24 -> 30 faces), 3 edits may hit other elements" and the editor
marks the edits. Maya and Houdini don't warn; it costs one int per domain.

Authored elements are owned by the document, so indices into it are stable by construction; no ids there either.

### Selection through a chain

`CubeGeometry -> SelectFaces -> ExtrudeFaces -> ExtrudeFaces -> DeleteFaces` (delete the final cap):

| Op | Face `Selection` after the op |
| --- | --- |
| `SelectFaces` (top, by normal) | top face = 1 |
| `ExtrudeFaces` | new cap = 1; new sides and the rest = 0. The original top face becomes the cap |
| `ExtrudeFaces` | extrudes the selected cap again; its new cap = 1 |
| `DeleteFaces` | deletes faces with selection >= 0.5 |

No index survives this chain or needs to: changing the cube's subdivisions or the extrude depth still deletes the right
face. Rules for ops, extending the geometry plan:

- An op that **creates** elements sets `Selection` to 1 on its main result (extrude: caps; offset: the new contour;
  inset: the inner faces) and 0 elsewhere.
- An op that **keeps** elements passes their `Selection` through (interpolated where elements are split or merged).
- Ops that need more than one result write **named selections** next to `Selection`, as Houdini's output groups do:
  `ExtrudeFaces` also writes `ExtrudeSides` when a name is given in its `StoreSidesAs` parameter. A selection parameter
  on consuming ops picks the attribute by name (default `Selection`), with a dropdown filled from the input as
  `ColorFacesFromAttribute` does.
- `SelectGeometry` with Combine (Replace, Add, Remove, Intersect) edits the current selection; `StoreSelection` copies it
  to a name so a later op can restore it after intermediate ops have overwritten `Selection`.

Clicking components in the Output window ends in the same place: "Store Selection" writes the clicked indices as a
`Selection` (or named) attribute override in `[EditCurves]`, and the graph takes it from there.

### 3. Serialization

`ShapeDocument` is a new input type, stored in the `.t3` as a compact, versioned, columnar object (the `List<float>`
approach, not the per-element objects `StructuredList.Write` produces):

```json
"Value": {
  "Version": 1, "Plane": { "Position": [0,0,0], "Rotation": [0,0,0,1] },
  "Contours":  { "Closed": [true, false], "AnchorOffsets": [0, 4, 7] },
  "Anchors":   { "Position": [x,y,z, ...], "In": [...], "Out": [...], "Type": [0,1,...] },
  "Overrides": { "InputCounts": { "ControlPoint": 24 }, "Domain": [0, 0], "Index": [17, 23], "Kind": [1, 2], "Delta": [x,y,z, ...] },
  "Attributes": [ { "Name": "Width", "Domain": "ControlPoint", "Type": "float", "Values": [...] } ]
}
```

- **In the `.t3`, not a sidecar file:** copy/paste, duplicating symbols, combining into a new symbol and undo all work
  through existing input mechanics.
- Hand-drawn shapes are hundreds of anchors, so size is fine. Imports with tens of thousands of anchors (SVG, CAD) stay
  file assets read by loader ops.
- `ShapeDocument` is not `CurveGeometry`: the wire type stays graph-only as the geometry plan requires. The op compiles
  the document into its reused `CurveGeometry` output.
- It is a **non-connectable editable parameter** (the precedent planned for `SceneToDrawSetup` in
  `Plan_SceneDocument.md`). Its parameter UI shows a summary and an "Edit in Output" button, not the data.
- Anchor `Type` (corner, smooth, symmetric) drives handle coupling while editing; the compiled curve only has handles.
- Values are immutable by convention: an edit builds a new document, so undo can keep the old one.
- Lives in `Core` (it must load in the Player), which is the justification for the `Core` addition. Run the
  interface-stability audit on the format before the first release.

### 4. Where editing happens

Editing is **editor-side**, in the Output window. There is no separate edit mode to enter: the left mouse button
belongs to editing, and the camera moves with the right button, as in the Unreal Editor (see
[Camera and input](#camera-and-input)). When the selected op has editable components, they show handles and can be
picked; double-clicking a shape focuses its op, as entering a card on the Board.

- **Projection:** a `PlaneViewProjection : ICanvasProjection` maps plane-local metres to screen through the view
  camera's matrices. With it, `CanvasPointHandle`, `CanvasItemPicker`, `SelectionFence`, `RectSnapping` and the polyline
  tool work on any plane in the 3D view. The Board is the special case of a top-down orthographic view.
- **Drawing:** handles, selected contours and hover are ImGui overlay (crisp, cheap, themeable). The geometry itself
  renders through the curve preview of the Output window (see [Phase 0](#phase-0-see-the-data)).
- **What is shown:** the op's evaluated output, read back from its `CurveGeometry` slot (CPU data, no readback cost),
  so procedural elements appear as editable, and overridden ones are marked.
- **Tools:** Select (V), Direct Select (A), Pen (P: click for corner, drag for smooth, click the first anchor to close),
  Polyline, Rectangle, Ellipse, Polygon, Add/Remove Anchor, Convert Anchor. Shortcuts follow Illustrator where they
  don't collide with existing ones.
- **Gizmo:** a bounding box with move, rotate and scale for the component selection (planned as the rotate/scale
  gizmo in `canvas-interaction.md`), in plane space.

### Camera and input

Decided 2026-11-20, to be carried into `Plan_CameraOverhaul.md`:

- **Edit is the default.** Left click and left drag select and edit; they no longer orbit.
- **Holding the right mouse button** switches to View/Fly temporarily: mouse look, W/A/S/D/Q/E, wheel for speed.
  Releasing it returns to editing. WASD is free for tools otherwise.
- **Orbit, pan, dolly** move to modifiers, as in Unreal and Maya: Alt+left orbits, middle drag (or Alt+middle) pans,
  Alt+right or the wheel dollies.
- A **View/Edit toggle** in the Output window toolbar keeps a dedicated navigation mode for users and for outputs
  without editable content, where left drag orbits as today. The long-term default is Edit.

This removes questions about entering an edit mode and about tool shortcuts colliding with WASD: tools own the
keyboard except while the right button is held.

### Generic component editing

Curves are the first editable domain, not the only one: posing joints, deleting points from a scan or nudging part
pivots need the same handles, picking, marquee, gizmo and undo. So the editor host is written against a small
contract, not against `CurveGeometry`:

- **Element sets:** for each editable domain, the op's evaluated output exposes elements by index with a position,
  optionally an orientation (joints, placements) and optionally bezier handles (curves).
- **Edits** are overrides keyed by `(domain, index)`: translate, rotate, delete, set attribute. `ShapeDocument` stores them
  per domain, so a later `[EditPoints]` or `[PoseSkeleton]` op uses the same document type, commands and UI.
- **Drawing** (pen, shapes) is the only curve-specific part.

The contract lives in `Editor/` until an op needs it; only `ShapeDocument` is in `Core`.

### 5. Construction planes and the orthographic camera

A plane is a `Pose` (`Core/Output/Pose.cs`): origin plus orientation. Plane-local X/Y is the drawing space.

- **Default plane** is world XY (front). Top (XZ) and side (YZ) are one click away.
- **The document stores its plane**, as a Fusion sketch belongs to a plane. Anchors are stored in 3D, so moving
  points off the plane (A5, heights in side view) is allowed; the plane is the drawing context, not a constraint.
- **Planes are passed between ops as geometry points** (position plus an `Orientation` attribute), like placements
  and spaces (`Plan_ProceduralGeometry.md`, "Geometry points are primary"). Section and projection ops take a plane as a point input; the editor can use any point output as a
  construction plane. No plane type.
- **Temporary planes** (Plasticity style) for one gesture or until dismissed: through three selected anchors, normal
  to a curve at an anchor (sections along a path), on a mesh face (needs face picking, late).
- **Look at plane:** aligns the viewer to the plane, orthographic. Needs the viewer ortho toggle and axis views from
  `Plan_CameraOverhaul.md` Phase 1 (its open question 3 is answered by this plan: yes, the viewer gets ortho first).
- **Grid** draws on the active plane, metric, with the Board's `MetricGridRaster` blending. This replaces the separate
  "XY grid" toggle idea: the grid follows the plane being edited, and stays the floor otherwise.

### 6. Selection and focus

Two selection planes, as on the Board (`selection.md`): **ops** (graph selection, unchanged) and **components**.

- A component target is `(SymbolChildId path, Domain, Index)`: anchor, handle (in/out), segment, contour, part.
  Targets beyond the current element count are dropped when resolved, as `SetupEntitySelection.TryResolve` does.
- Held in a shared `CanvasSelection` (`SelectionSet<ComponentTarget>`), which `selection.md` already plans for the Board.
- **Focus** follows the Board: double-click a shape to enter component editing for its op, Esc to leave, everything
  else fades like the Board layer does while a card is entered. Only one op is in component focus at a time.
- **Editor selection vs. data selection.** The component selection is transient UI state. "Store Selection" writes it
  as a named `Selection` attribute override in the document, so ops downstream read it (B5: offset keeps the
  selection, extrude uses it). Ops propagate `Selection` as the geometry plan specifies.

### 7. Undo and redo

- Discrete edits and gestures replace the document value through `ChangeInputValueCommand`, with the gesture pattern
  from the Board: snapshot on press, mutate a working copy live, one command on release.
- Documents are immutable by convention, so a command holds two references, not copies. Commands store the op by
  `SymbolChildId` and resolve it at call time (AGENT_INSTRUCTIONS undo rules).
- Selection changes are not undo steps (consistent with the graph).

### 8. Attributes

- **Authored:** float, vec2/3/4 and int attributes on anchors, contours and parts, edited in the parameter panel for
  the component selection (multi-edit like `Plan_MultiSelectionEditing.md`).
- **Procedural:** `ResampleCurves` writes `U` (normalized arc length per contour), `Distance` (metres from start) and
  `ContourIndex`. `CurvesToPoints` gets the explicit attribute mapping the geometry plan already asks for (which
  attribute lands in `Color`, `F1`, `F2`), so U reaches GPU points and gradient ops (A8).
- **Inspection:** the `CurveGeometry` output UI lists attributes per domain with a table (the spreadsheet viewer of
  `Plan_StructuredData.md` Phase 2 is the long-term form). Hovering a component in edit mode shows its attributes.

### 9. Reference images

A `[ReferenceImagePlane]` op draws an image on a plane in world units. Image, `MetersPerPixel`, crop and opacity are
its inputs; the scale line tool (from `SetupOutputView.ScaleLine.cs`) sets `MetersPerPixel` from two clicks and a typed
length. Lock means "not pickable in edit mode", so tracing never grabs the image.

It is an op, not editor state, so it renders in the output, survives in the project and can be faded per view with
the gizmo visibility. The Setup's `ReferenceImage` stays a venue entity; the two share the crop and scale line code.

## Validation scenarios

Thought experiments, not planned features. Each must be buildable by adding ops (or a package) without a new
connection type and without a new editor concept. When a scenario fails that test, the design changes, not the
scenario.

**1. House section to SVG.** Load an OBJ of a house, cut a section as curves, clean up, style the lines, export SVG.
- `LoadObjGeometry` -> `SectionGeometry` (new: mesh x plane -> closed contours, plane as a geometry point input, source part
  and face attributes carried onto contours) -> cleanup ops (`SimplifyCurves`, `WeldCurveEnds`, `FilterContours` by
  length) -> manual fixes in `[EditCurves]` -> style -> `ExportSvg` (new, file writer with a trigger, like other
  exporters).
- Stroke style is **attributes with agreed names** (`StrokeWidth`, `StrokeColor`, `Dash`), read by `CurvesToPoints`
  for display and by `ExportSvg`. A convention, not a type.
- Line effects that must survive the export (dashes, jitter, offsets) are CPU curve ops. GPU line effects are for
  display only.
- Weak spot: index overrides on a section are fragile, since moving the plane changes every contour. Fine for cleanup after
  the plane is settled; Bake for heavier edits.
- Verdict: fits.

**2. Slice a posed animal.** Load a glTF character, pose it, take a clip, slice it into curves.
- `LoadGltfScene` -> `PickGeometryFromScene` (`Plan_SceneDocument.md`) -> pose -> CPU skinning -> `SectionGeometry`
  with many planes (geometry points from any point generator) -> one part per slice.
- **Skinning is GPU-only today.** Needs a CPU `SkinGeometry` (skin weights as point attributes, already a long-term
  item in the geometry plan) or an explicit readback op. Either is an op, not a concept.
- **Posing** is editing joint orientations: a domain with index, position and orientation. It works only because the
  editor host is generic (see [Generic component editing](#generic-component-editing)); a curve-only editor would
  fail this scenario.
- Verdict: fits with the generic host; CPU skinning is the real cost.

**3. Point cloud to hull.** Load a point cloud, cut away noise, reconstruct a low-poly hull.
- Loader (PLY/LAS, package) -> `MeshGeometry` with no faces (already shown as "point cloud" by the stats UI), color as
  a Point attribute -> `RemoveOutliers` (statistical, KD-tree helper) -> manual cut -> `ConvexHull` / alpha shape /
  Poisson (package) -> `DecimateGeometry` (planned).
- **Manual cuts on millions of points** can't be per-element overrides. Draw a closed curve in `[EditCurves]` and feed
  it to `FilterGeometryByCurve` (inside the curve's prism along its plane normal). **Curves double as selectors and
  cutters**: the same drawing tools, no lasso concept.
- Preview of millions of points needs the GPU path (`GeometryToMesh` or a points conversion), and picking needs the
  screen-space grid from [Risks](#risks).
- Verdict: fits; overrides are for few elements, bulk edits go through drawn selectors.

**Rules that fall out**
- Planes and placements are geometry points; CPoints only cross to and from the GPU.
- Conventions are attribute names, not types.
- Overrides are for tens of elements; bulk edits use curves as selectors.
- Exporters are ops with a trigger.
- GPU<->CPU crossings are explicit ops (as in the geometry plan).
- The editor host knows element sets, not curves.

## Op gaps

Mapped from the two workflows; all on `CurveGeometry` / `MeshGeometry`, CPU, with selection propagation.

| Op | Need | Notes |
| --- | --- | --- |
| `RectangleCurves`, `EllipseCurves`, `PolygonCurves` | B1, B4 | Also drawing tools in `[EditCurves]`; the ops are the procedural form |
| `ResampleCurves` | A4, B2 | By distance or count, start offset, writes U/Distance, keeps corners optionally |
| `CurvesToPoints` attribute mapping | A7, A8 | Planned in the geometry plan |
| `SweepCurves` | A6 | Profile along path to `MeshGeometry`, one part per segment optional (planks). GPU `[ExtrudePointCurves]` exists |
| `InsetFaces` / `ShrinkParts` | A6 | `ExplodeGeometry` partly covers shrinking parts |
| `SelectGeometry` | B5 | Planned in the geometry plan |
| `ExtrudeFaces` | B5 | Uses the face selection, selects new caps, optional named sides selection |
| `DeleteGeometry`, `StoreSelection` | B3, chains | Delete selected elements; copy `Selection` to a named attribute |
| `CombineGeometry` | A, B | Planned in the geometry plan |
| `TransformCurves`, `CombineCurves` | general | Planned in the geometry plan |

Existing and sufficient: `CurvesToGeometry` (fill, extrude, bevel), `OffsetCurves`, `SubdivideCurves`, `BevelGeometry`,
`PlaceGeometryAtPoints`, GPU instancing at points, `GeometryToMesh`.

## Phases

Each phase ships on its own and is useful without the next.

### Phase 0: See the data (implemented 2026-11-20, needs a manual test pass)

Done: internal composites `_OutputPreviewCPoints`, `_OutputPreviewCurves` and `_OutputPreviewGeometry`
(`Operators/Lib/Symbols/render/gizmo/_/`) rendered through `CommandPreview<T>` (`Editor/Gui/OutputUi/Previews/`);
Points/Table for point lists, Render/Stats for curves and geometry, a new `CurveGeometryOutputUi`. Test set
`.tests-manual/output-window-geometry.md`. Curves show lines only; anchors and handles wait for Phase 3.

- Output window previews for CPoints, Curves and Geometry (render plus stats/table), generalizing
  `PointBufferVisualizer`.
- `CurveGeometryOutputUi` with counts, attributes per domain, parts.
- Milestone: every geometry op can be inspected without adding draw ops.

### Phase 1: Orthographic viewer and planes
- Viewer ortho toggle and Front/Top/Side views (`Plan_CameraOverhaul.md` Phase 1).
- Unreal-style input: right button flies, Alt+left orbits, View/Edit toggle in the toolbar.
- Grid on a chosen world plane.
- Milestone: B4's front view looks like a drawing.

### Phase 2: Extract the canvas editing kit
- Move gesture undo, `PressHandoff`, marquee and the polyline tool from `SetupOutputView` into `CanvasEditing/`.
- Add `PlaneViewProjection`.
- No user-visible change; the Output Setup test sets must still pass.

### Phase 3: Draw curves
- `ShapeDocument` type with serialization, `[EditCurves]` without input (authoring only).
- Edit mode in the Output window: pen, polyline, rectangle, ellipse, polygon, select, direct select, marquee, delete,
  convert anchor, snapping to grid and anchors, typed values. Undo.
- Milestone: B1 and B4 drawn by hand.

### Phase 4: Reference images
- `[ReferenceImagePlane]` with scale line, lock and opacity.
- Milestone: A1 to A3.

### Phase 5: Overrides on procedural input
- Overrides in `[EditCurves]` with an input, keyed by index, topology-change warning, Bake.
- `ResampleCurves` with U, `CurvesToPoints` mapping, `SweepCurves`.
- Milestone: A4 to A8, B2 and B3.

### Phase 6: Selection into the graph
- Component selection stored as attributes, `SelectGeometry`, `ExtrudeFaces`, `InsetFaces`.
- Milestone: B5 and B6; both workflows complete.

### Phase 7: Construction comfort
- Temporary planes (three points, normal at anchor, mesh face), snapping to other geometry (endpoints, midpoints,
  intersections), dimensions while drawing.

### Later
- 2D curve booleans, shape keys, SVG export of documents.

Each user-visible phase adds a manual test set in `.tests-manual/` and a release-notes entry.

## Decisions

1. **Documents are stored in the `.t3`**, not in a sidecar file (2026-11-20).
2. **Reference images are an op**, `[ReferenceImagePlane]` (2026-11-20).
3. **No `[EditPoints]` for `StructuredList<Point>`.** Points are edited as curve anchors (or as a geometry point domain)
   and converted late (2026-11-20).
4. **Unreal-style camera, Edit by default** (2026-11-20), see [Camera and input](#camera-and-input).
5. **One op for drawing and editing.** `[EditCurves]` draws without an input and edits its input when one is connected
   (2026-11-20).

## Open questions

1. **Scenario coverage.** Add further validation scenarios here and to `Plan_ProceduralGeometry.md` (which has three)
   whenever a new use case comes up; each must name the rule it tests.

## Risks

- **Selection propagation** has to be right in every op that creates or keeps elements. Cheaper than ids, but still a
  rule for every op author: add it to the operator conventions, and give the geometry output UI a selection overlay so
  mistakes are visible.
- **Picking cost** with large inputs: hit-testing thousands of anchors per frame. Use a screen-space grid built once
  per document or camera change.
- **Two editing hosts** (Board and Output window) drifting apart if Phase 2 extracts only part of the kit.
- **Scope creep** toward a modeller. The scope table is the reference when a feature request arrives.
