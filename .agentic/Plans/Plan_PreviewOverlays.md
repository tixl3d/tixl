# Preview overlays: display toggles and graph-drawn selection

Status: **Phases 1-4 done** (2026-11-21); decision 3 (defaults per preview type) still open. Drafted 2026-11-21.

**What shipped:** `Core/Rendering/PreviewOverlayState` on `EvaluationContext.PreviewOverlay`; `[GetPreviewState]`
(flow/context; one bool output per overlay plus `Selected`/`Hovered`, `DirtyFlagTrigger.Always` like
[GetRenderPassState]); `OutputWindowState.PreviewOverlays` and the toolbar popup (eye icon); `GeometryElementSubset`
(Editor) and `PreviewPointGeometry` for the subsets; `[_DrawGeometryHighlight]` (edges/curves as lines, all points as
pixel dots, a faint pass without and a bright pass with depth test); [CurvesToPoints] `Sampling` AllPoints;
[FilterGeometry]; [ColorGeometry] Domain Auto; `_OutputPreviewGeometry` wired to the state (Wireframe drives
`PolygonEdges`, Points an [Execute] around the dots, PointIndices `VisualizeMesh.ShowIndices`, Selection and Hover
branches, the Selection attribute through FilterGeometry -> ColorGeometry with the red/white/blue gradient);
new `_OutputPreviewPoints` and `_OutputPreviewMesh`, and a hover branch in `_OutputPreviewCPoints`.
`GeometrySelectionOverlay`, `SelectionOverlayStyle` and `PreviewProjection` are deleted. The Normals flag exists but
no preview draws normals yet.

## Goal

The output previews for geometry, meshes and points draw selection and hover highlights with ImGui on top of the
rendered image (rings, dots, thick lines, translucent fills). They ignore depth, look unlike the rest of the render,
and can only be styled in C#. Move all of it into the preview graphs:

1. **Display toggles per output window** (Wireframe, Points, Point indices, Selection, Hover, ...), set from a popup
   menu in the output toolbar and passed to the preview in a typed preview state on the context, the way
   `RenderPassState` travels.
2. **Selection and hover as data**: the editor builds small subset geometries from the table state and puts them into
   the same preview state; the preview op reads them and draws them with ordinary draw ops, depth-aware.
3. **The selection attribute** written by [SelectGeometry] is drawn by the preview graph itself, through a new general
   [FilterGeometry] op.
4. The same scheme for the point and mesh previews; then the ImGui overlays are deleted.

## What exists today

- **Gizmo toggle as a model.** `OutputWindowState.ShowGizmos` (`Editor/Gui/Windows/Output/OutputWindowState.cs`) is
  persisted per output window in the composition's `.t3ui` settings, toggled in `OutputWindow.Toolbar.cs` and copied
  into `EvaluationContext.ShowGizmos` in `OutputWindow.DrawOutput.cs`.
- **Typed context state as a model.** `EvaluationContext.RenderPass` (`RenderPassState`) is a typed struct on the
  context; [SetRenderPassState] sets it around a subgraph and restores it afterwards, [GetRenderPassState] reads one
  field chosen by a `Select` enum. The preview state follows this pattern (maintainer decision 2026-11-21) instead of
  the string-keyed `BoolVariables` / `ObjectVariables`.
- **Preview ops.** `CommandPreview<T>` (`Editor/Gui/OutputUi/Previews/CommandPreview.cs`) connects the shown value to
  one input of a parentless op and renders its `Command` output:
  - `_OutputPreviewGeometry` (Geometry): `GeometryToMesh` -> `VisualizeMesh`, `CurvesToPoints` -> `DrawLines`,
    `CurvesToPoints` -> `DrawPoints`;
  - `_OutputPreviewCPoints` (CPU point lists);
  - [VisualizePoints] (point buffers) and [VisualizeMesh] (MeshBuffers), used directly.
- **ImGui overlays to replace.** `GeometrySelectionOverlay` (table selection, Selection attribute, hover),
  `SelectionOverlayStyle.DrawHoverRing` in the buffer, point list and mesh views, and `PreviewProjection`, which maps
  world positions onto the render for them.
- **Draw ops ready for overlays.** [DrawPoints] `SizeMode` Pixels, [DrawLines] (`EnableZTest`, `ShrinkWithDistance`),
  [CurvesToPoints] `PolygonEdges` (All / Sharp), [_DrawPointInfo] for index labels.
- **Subset helpers.** `Lib/Utils/GeometryPartSubset`, `GeometryPolygonFilter` and `GeometryCurvePassThrough` copy parts
  of a geometry with their attributes.

## Design

### Preview state on the context (typed, like `RenderPassState`)

- **Core:** a `PreviewOverlayState` on `EvaluationContext` (next to `RenderPass`), default "nothing requested":
  - `Overlays` (`[Flags] enum`: Wireframe, Points, PointIndices, Selection, Hover, Normals);
  - `Selected` and `Hovered` (Geometry, null when empty), see below;
  - room for later fields (e.g. hovered point list for point previews) without changing op inputs.
  The Player never sets it, so exported projects render previews' default branches only (they aren't exported anyway).
- **Ops (Lib):** `[GetPreviewState]` with a `Select` enum for the flags (bool output) like [GetRenderPassState], and
  `Selected` / `Hovered` Geometry outputs. Preview composites read everything through it; their inputs don't change,
  and `CommandPreview<T>` keeps binding one value.
- **Lifetime:** the output window sets the state right before the preview renders and restores the previous value
  after, like `SetRenderPassState` does, so nothing leaks into other evaluation.

### Display toggles (per output window)

- `OutputWindowState` gets a `PreviewOverlays` flags field, persisted per output window like `ShowGizmos`
  (maintainer decision 2026-11-21: window specific, not global).
- **Popup menu with icons** (decision 2): one icon button next to the gizmo toggle opens a popup listing the overlays,
  each with its icon and a check state. Shown only when the presented type has a preview (Geometry, MeshBuffers,
  point buffers, point lists).
- Defaults per preview type are still open (decision 3).

### Selection and hover as subset geometries

- **Why not attributes on the shown geometry:** hover changes with every mouse move. Writing it into the main
  geometry would invalidate [GeometryToMesh] and everything downstream for the whole mesh on each move.
- The editor builds two small geometries from the table state, only when the selection, the hovered row or the shown
  geometry changes:
  - **Selected**: the selected rows of the current domain: points as a point-only geometry, edges as two-point
    curves, primitives as copies of the primitives, parts as their primitives;
  - **Hovered**: the same for the one hovered row.
  - Built by a new `GeometryElementSubset` (Editor, or Lib/Utils if ops need it), reusing its buffers; a new instance
    only when the content changes, so async consumers stay valid.
- They travel in `PreviewOverlayState.Selected` / `.Hovered` and come out of [GetPreviewState].
- The preview graph draws them with existing ops:
  - faces: [GeometryToMesh] -> [VisualizeMesh] tinted;
  - edges and outlines: [CurvesToPoints] (`PolygonEdges` All) -> [ListToBuffer] -> [DrawLines];
  - points: [DrawPoints] with `SizeMode` Pixels.
  - Depth: a dim pass without depth test plus a bright pass with depth test, so hidden selected elements read as
    behind. Selection and hover colors are defined in the preview op, not in C#.

### Selection attribute from [SelectGeometry]

- A new general op **[FilterGeometry]**: keeps the elements of one domain whose float attribute is above a threshold
  (Invert keeps the others), like Houdini's Blast. Useful on its own for deleting or isolating a selection.
- `_OutputPreviewGeometry` runs the shown geometry through [FilterGeometry] (keep `Selection` != 0) in a branch
  behind the Selection overlay flag and draws the result like the table selection.
- **Soft selections fade** (decision 4): the kept elements are colored by their weight with [ColorGeometry] and a
  gradient, like the table's value-change colors: 0 transparent, 1 the selection color, above 1 towards blue, below 0
  towards red. Weights outside 0..1 come from extrapolating ops and should stand out.

### Points and meshes

- **Point buffers / point lists:** the hovered and selected rows become a small point list (copied from the readback
  or the CPU list) passed as an extra input. [VisualizePoints] is used directly today, so this needs a thin
  `_OutputPreviewPoints` composite around it with the extra inputs and toggles.
- **MeshBuffers:** the hovered vertex as a one-point list. Triangle hover isn't needed yet (decision 5).

### Removal

- Delete `GeometrySelectionOverlay`, the hover-ring calls and `SelectionOverlayStyle` once every view draws through
  its preview graph. `PreviewProjection` then only serves the contained view rectangle of `PreviewSidebar`; remove it
  if nothing else projects.

## Phases

### Phase 1: Toggles
- `PreviewOverlayState` on `EvaluationContext`, `[GetPreviewState]`, `PreviewOverlays` flags on `OutputWindowState`,
  the popup menu, and setting/restoring the state around the preview render.
- `_OutputPreviewGeometry` reads Wireframe, Points and PointIndices for its existing branches.
- Done when: toggling in one output window changes only that window's preview and survives a restart.

### Phase 2: Table selection and hover through the graph
- `Selected` / `Hovered` in the preview state; `_OutputPreviewGeometry` draws them via [GetPreviewState].
- `GeometryElementSubset` builds them per domain; the geometry view passes them instead of drawing ImGui overlays.
- Delete the geometry part of `GeometrySelectionOverlay`.
- Done when: selected and hovered points, vertices, edges, primitives and parts highlight depth-aware for a cube,
  text curves and an extrusion, and hovering a row doesn't recompile the main mesh (check with `getUpdateStats`).

### Phase 3: [FilterGeometry] and the Selection attribute
- The op with Domain, Attribute (type-ahead), Threshold, Invert; descriptions and a test step.
- Preview branch behind the Selection flag with the fading gradient; delete `DrawSelectionAttribute`.

### Phase 4: Points and meshes
- `_OutputPreviewPoints` composite for point buffers and lists with hover and selection inputs; MeshBuffers hover.
- Delete the remaining ImGui overlays and `SelectionOverlayStyle`; decide on `PreviewProjection`.
- Docs: release notes, `.tests-manual/output-window-geometry.md` and `output-window-buffers.md`.

## Decisions

1. **Selected/Hovered transport** (2026-11-21): typed, like the render-pass settings: a `PreviewOverlayState` on the
   context read by [GetPreviewState], not string-keyed context variables and not extra inputs on the preview ops.
2. **Menu shape** (2026-11-21): a popup menu with icons.
4. **Soft selections** (2026-11-21): fade with the weight; above 1 towards blue, below 0 towards red, like the table's
   value-change colors.
5. **Mesh triangle hover** (2026-11-21): not needed yet.

## Open decisions

3. **Defaults per preview type.** E.g. wireframe on for meshes, indices off for points. Undecided; start with the same
   defaults for all and adjust after using it.

## Risks

- `PreviewOverlayState` is a Core change; keep it a plain struct with value semantics like `RenderPassState`, so
  restoring it after the preview render is a copy.
- The preview composites grow; keep each branch behind its toggle so disabled overlays cost nothing.
- Editing composites as `.t3` JSON is error-prone; build the preview graph changes in the editor where possible and
  verify each phase with the debug bridge (`screenshot`, `getUpdateStats`).
