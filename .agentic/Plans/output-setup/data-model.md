# Output Setup — Data Model

Status of the entity model behind the [output-settings spec](output-settings-spec.md), grounded in the
**as-built** classes under `Core/Output/`. Purpose: spot gaps and contradictions **before** implementing
the new sidebar. Legend: ✅ exists as needed · 🟡 partial · ❌ not defined · ⚠️ contradicts a prior decision.

Design decisions this doc is measured against (from the design sessions / prototype README):
- Pipeline in four spaces: **`Texture → Slice → Region → Surface → Mapping → Output`**.
- **Unify Surface & Region** into one recursive node; "Region" = a *coplanar child* Surface; a node with
  its own placement is a **plane-root** (physical, calibratable). Projection is owned per plane-root and
  inherited by coplanar children; content is owned per leaf.
- **Slice** = a named sub-rect of a content texture (a node; can be `Unused`). `Slice→Region` is the *send*.
- **`SendToOutput` loses its `OutputRef`**; its target becomes a polymorphic path: an **Output** (direct
  full-frame pipe) or a **Surface/Region** (mapped). Routing to projectors lives in the Mapping, not the op.
- **Live vs stored:** content sources are *live graph ops* (sink registry); surfaces/outputs/references/props
  are *stored setup entities* (`.setup.json`). Device bindings are *per-machine* (`outputs.machine.json`).

---

## 1. As-built entities (what exists today)

| Entity | Class | Fields (abridged) | Stored in |
|---|---|---|---|
| Setup | `Setup` | `ReferenceImages[] Surfaces[] Outputs[] Props[]`, `Version=1` | `.meta/<name>.setup.json` |
| Surface | `Surface` | `Id Name Type="Rect" SizeInMeters PixelsPerMeter`, `OutputMappings[]`, `Reference?`, `Placement?` | Setup |
| — mapping | `Surface.OutputMapping` | `OutputId Mode="CornerPin" Quad[4](outpx) SourceQuad[4](uv)` | Surface |
| — trace | `Surface.ReferenceBinding` | `ImageId Quad[4](refpx) Annotations[]` | Surface |
| — placement | `Surface.StagePlacement` | `Pose Pivot` | Surface |
| Output | `OutputDefinition` | `Id Name Kind(Default/Format/Projector/Display) CanvasResolution Camera?` | Setup |
| — camera | `OutputDefinition.ProjectorCamera` | `Pose? Lens? CalibrationPoints[] ResidualPx Manual{Position,Target,FovY}` | Output |
| Reference image | `ReferenceImage` | `Id Name Kind(Photo/Plan) FilePath W H MetersPerPixel` | Setup |
| Prop | `Prop` | `Id Kind(Person) Position HeightInMeters` | Setup |
| Device binding | `DeviceBinding` in `MachineConfig` | `OutputId DisplayName DisplayIndex Fullscreen` | `.meta/outputs.machine.json` (per-machine) |
| Active setup | `ActiveSetup` (static) | `Current:Setup? Machine:MachineConfig?` + `TryFindSurface/Output` | runtime |
| Content sink | `IOutputSink` / `OutputSinkRegistry` | `GetOutputId/GetSurfaceId/GetColor/GetContent + InvalidateContent` | live (graph) |
| — op | `SendToOutput` | inputs `Texture, OutputRef:Guid, SurfaceRef:Guid, Color`; **no output slot** | graph |
| Selection | `SetupEntitySelection` (per window) | `SelectedKind{None,ReferenceImage,Surface,Prop,Output} SelectedId` | runtime |

Serialization is hand-rolled (`WriteToJson`/`ReadFromJson` per class via `OutputJson`/`T3.Serialization`).
Any new field or entity must be added there **and** kept back-compatible (readers default missing keys — see
`OutputMapping.SourceQuad` reading `FullSourceQuad()` when absent, the pattern to copy).

---

## 2. Target model vs. as-built — gaps & contradictions

### 2.1 Slice — **RE-REVISED (2026-07-27): setup-side, as built — the 07-22 op-side decision is reversed**

The implementation went setup-side (`Setup.ContentSources` + `Setup.Slices`; `Surface.SliceId` points at a
slice; sources keyed by the op's `SymbolChildId`), and after review this is **blessed as the shipping model**:
- **Why setup-side won in practice:** routing lives where the rest of the calibration is edited, survives
  re-instantiation and hot reloads without an op eval, duplicates with the venue, and reads sensibly from the
  file while nothing is instantiated. Slices as named entities (with `Unused` = unreferenced) fell out
  naturally; op-side would have re-scattered them across ops.
- **Accepted costs (documented, not bugs):** duplicating a `SendToOutput` op does *not* carry routing (the
  source keys on the original `SymbolChildId` — the duplicate appears as a new unbound source); the
  `ContentSourceSync` adopt/rename/cascade subsystem is permanent (now frame-driven, save-debounced); a
  composition instanced twice yields ambiguous `SymbolChildId` sinks (first-registered wins — a known open
  edge, fix when multi-instancing sends becomes a real use case).
- The flow-view connection lines render this model: ContentSource → Slice → Surface/Output edges are all
  setup data; only the source↔op link crosses into the live graph.

<details><summary>Superseded 07-22 decision (op-side), kept for history</summary>
The mockup shows `Atlas → {Poster Left, Poster Right, Unused}` — **named slices under a content source**,
one *unbound*. Today the only slice concept is `OutputMapping.SourceQuad`: a single, **unnamed** UV quad on
the surface's mapping, with no `Unused` notion.
- **Decision (revised 2026-07-21):** a `Slice` is **op-side content data, not a setup entity** — content is
  venue-independent (§2.4) and must duplicate/drive with the op. **Model (a): one send-op per target** — a
  `Slice` = the `{ SourceRect(uv), TargetId:Guid }` on a single `SendToOutput`; `TargetId` binds to a
  Surface/Region or Output (empty = `Unused`), resolved against `ActiveSetup` each eval.
- The `CONTENT` panel groups send-ops by shared upstream texture for the "Atlas → sends" visual (a view, not
  a stored parent). Fork closed 2026-07-22 (model (b), an op-held slice-list, dropped).

</details>

### 2.2 Region / Surface unification — ❌ not defined · **RESOLVED: nested tree + explicit `Kind`**
`Surface` is **flat**: no parent, no plane-root flag. Sub-regions can't be expressed, and the "physical (m)
vs layout (px)" distinction the spec draws in **callout 22** has no model form.
- **Decision (4.2):** keep the flat `Setup.Surfaces` list; each `Surface` gains a **`ParentId:Guid`**
  (`Empty` = root). Nesting via parent-ids (not embedded `Children`) — easier serialization, reorder, and
  stable references.
- **Decision (4.3):** the physical-plane vs coplanar-layout distinction is an **explicit `Kind` field**
  (`Physical | Layout`), chosen at creation per callout 22 — *not* inferred from whether a pose is set.
  `Physical` carries a stage pose (meters); `Layout` is coplanar in its parent, no independent pose (px/rem).
- **Region owns only its content** (a Slice binding), never a mapping — see 2.3.

### 2.3 Mapping — 🟡 partial; keep it, and make it **rich** (corrected)
A mapping is a per-`(surface, output)` **edge**, and `Surface.OutputMappings[]` is **already exactly that
list** — a surface with two entries = two projectors (a cube's shared face → soft-edge). Multi-projector
per surface is already expressible; earlier notes described it as singular, which was wrong.
- **Move off the mapping:** only `SourceQuad` — "*which part of the source texture*" is an orthogonal axis
  and belongs to the `Slice` (§2.1). Everything else about "how this surface lands in projector P" stays.
- **Grow the mapping** into the full refinement stack (spec "Additional notes" + the cube case):
  `OutputMapping = { OutputId, Mode, Quad[4], Warp?, Mask?, CornerColors[4]? }`
  - `Mode`: `CornerPin` (manual quad) **or** `Calibrated` (inherit the output's solved projector camera)
  - `Warp`: optional lattice — resolution (e.g. 2×2), points w/ positions+tangents, `Linear|Hermite`;
    **resample-on-resolution-change** (don't lose the existing definition)
  - `Mask`: optional (soft-edge feather / arbitrary)
  - `CornerColors`: optional per-corner color (lift/gain or multiply, barycentric-interpolated)
- **Regions need no mapping of their own.** A region is a sub-rect of the surface's content canvas; it rides
  the surface's mapping(s), differing only in content (its slice). The **hard-split wall** (left→ProjA,
  right→ProjB, hard seam) is **two *masked* mappings on one surface**, not two regions — `Mask` subsumes it,
  so the earlier "per-region mapping override" idea is **dropped**.
- **Storage sub-choice (open):** keep mappings surface-side (`Surface.OutputMappings[]`, current) vs a
  `Setup.Mappings[]` join table `{SurfaceId, OutputId, …}`. Surface-side is fine; a join table reads more
  naturally for the by-Output sidebar view. Low stakes — decide when building the view.

### 2.4 `SendToOutput` — **RE-REVISED (2026-07-27): superseded by §2.1 — content and targeting are setup-side.**
`SendToOutput` stays `{ Texture, Update, Color }` with no target param; routing is `Surface.SliceId` /
`OutputDefinition.SliceId` in the Setup. The section below records the superseded 07-22 reasoning.

<details><summary>Superseded 07-22 decision (op-side targeting), kept for history</summary>
The earlier "setup owns targeting, keyed by op-id-path" fought the existing venue-swap design. `Setup.Duplicate`
preserves entity GUIDs *precisely so ops can reference setup entities by Guid across venues* (`UseProjectorCam`
already resolves an `OutputRef` Guid against `ActiveSetup` each eval). And `Setup` is "everything re-done when
the physical situation changes" — targeting is **not** re-done per venue. So:
- **Targeting = `TargetId:InputSlot<Guid>` on the op**, resolved against `ActiveSetup` each eval (never cached).
  A Surface/Region id → mapped path; an Output id → direct full-frame pipe. Drop `OutputRef`/`SurfaceRef` in
  favour of this one polymorphic ref (+ keep `Texture`, `Color`).
- **Duplication** copies the param → the dup points at the same target (then re-point). **Procedural addressing**
  (a known future ask) is free later: `TargetId` is a real InputSlot → *connect* a Guid to drive it. Nothing
  built now, nothing foreclosed.
- **Content is NOT in the Setup** — no `ContentSource` stored entity. The op's child-name labels the `CONTENT`
  row; it registers with `OutputSinkRegistry` as now. (Reverses the op-id-path keying — no orphan problem.)
- **Slice multiplicity — RESOLVED (a), 2026-07-22:** **one send-op per target**, op =
  `{ Texture, TargetId:InputSlot<Guid>, SourceRect, Color }`. `TargetId` picked via a custom-dropdown (Guid
  input values persist on the op — confirmed). No variable per-op list needed. The `CONTENT` panel renders
  the "Atlas → sends" tree as a **group-by-shared-upstream-texture view**, not a stored parent.
- **No migration on this branch** — pm entities can change freely; rebuild examples after merge.

</details>

### 2.5 Direct-to-output (surfaceless pipe) — 🟡 half-present
The full-frame path exists in `OutputManager` (`_fullscreenNdc`), and `OutputDefinition.Kind` already
distinguishes `Default/Format/Projector/Display`. Missing: **`NDI`/`Spout` kinds** (spec callout 24) and the
model-level statement that *these kinds are direct-only* (no surfaces). Target-path picker must filter by
kind (a plug-only NDI output never offers surface mapping).

### 2.6 Selection model — 🟡 too narrow · **plan in [`selection.md`](selection.md)** (4.5: do not defer)
`SetupEntitySelection` is single kind+id over `{ReferenceImage, Surface, Prop, Output}`. The spec needs
Slice/ContentSource kinds, multi-select, and **sub-element addressing** (corners, annotation lines, lattice
points). Agreed this can't be deferred. Plan: **two selection planes that never mix** —
- **Entity plane** (tree): whole entities; drives the panel + entity drag-drop.
- **Sub-element plane** (canvas): handles (corners/endpoints/lattice points); drives on-canvas editing.
- One address form `Target { kind, entityId, part: None|Corner|Annotation|LatticePoint, index }`, ordered
  (edges are drag-only handles, not selectable — see `canvas-interaction.md`)
  (first = primary). Selecting an entity populates the canvas plane for it; selecting sub-elements never
  scrambles the entity set. Full design in [`selection.md`](selection.md).

### 2.7 Output ↔ machine display binding — ✅ model ready, 🟡 UI missing
`DeviceBinding`/`MachineConfig` fully model "which connector on which computer" (name-first, index-fallback,
per-machine, gitignored). The spec's callout 24 side-note ("an output needs to be bound to a display and this
should be indicated") is a **UI gap only** — surface the binding state on the Output row, and offer bind in
the `+` menu (the existing `ResolutionHandling.DrawOutputBindingMenu` is the hook).

### 2.8 Props — 🟡 minimal
`Prop` is `Person`-only with `Position + HeightInMeters`. Spec callout 26 wants json-defined templates
`{assetPath, height, pivot}`. Small, future — noted, not blocking.

### 2.9 Reference-image canvas (m-space, multi-image assembly) — ❌ not modeled
Spec callout 25.4 wants a *Reference Image Canvas* where several images (photos, plans) are placed/scaled in
**meters**. Today `ReferenceImage` has `MetersPerPixel` (a per-image scale) but **no canvas placement**
(position/rotation/scale of each image on a shared m-canvas), and no `AnnotationLine`-on-canvas that isn't
attached to a surface's `ReferenceBinding`. Needs a placement per reference image + free annotations.

---

## 3. Components & classes (the UI / ImGui side)

Where the sidebar and its behaviors will live, and what's reusable vs new.

**Existing UI classes (reuse):**
- `OutputWindow` (+ `.DrawOutput`, `.State`, `.Toolbar` partials) — the host window & toolbar.
- `OutputSetupModeView` — focus routing (sink-focus vs picked entity) + owns the side panel & selection.
- `SetupPanel` — **the tree** (today: outputs→surfaces nesting). This is the class the new spec mostly rewrites.
- `SetupOutputView` — the output/content canvas (corner-pin editing, ScalableCanvas + `ICanvasProjection`).
- `ReferenceImageView` — the reference/straighten canvas.
- `OutputManager` — render/composite + present; `RenderWarpedTexture`, `_fullscreenNdc` full-frame path.
- `SetupEntitySelection` — selection (extend per 2.6).
- `ResolutionHandling` — resolution presets + `DrawOutputBindingMenu` (display binding).

**Existing helpers (reuse):**
- `CustomComponents`: `DrawMenuGroupLabel`, `DrawSubMenu`, `DrawMenuItem`, `StateButton`, `ContextMenuForItem`
  — the app-menu grammar the spec's context/add menus (callouts 6.2, 19, 22, 24) should use.
- `Interaction/CanvasEditing/`: `CanvasPointHandle`, `CornerPinHandles`, `ICanvasProjection`,
  `ScalableCanvasProjection`, `ICanvasPointSnapper` — for on-canvas drag (corners, future lattice/annotations).
- `T3Ui.UiScaleFactor` — every px literal in the spec's style notes (`~25px` indent, gutters) × this.

**New components to build (spec calls these out):**
- ❓ **Tree/outliner row** — a reusable row with: left disclosure (`Icons.ChevronDown/Right`), left in-gutter,
  icon+name, right out-gutter, `+`/badge. No general tree-row helper exists today (`SetupPanel` hand-rolls
  rows) → factor one out. Carries the state matrix (default/unreferenced/hover/cross-highlight/select/drag/drop).
- ❓ **Vertical separator** starting an icon group (callout 16) — "should become a CustomComponent if not
  already defined" → **verify; likely new.**
- ❓ **Gutter reference indicators** (in ← / out →) with hover-to-cross-highlight + tooltip thumbnail
  (callouts 10, 11, 20, 21) — new; depends on the selection/hover cross-highlight rule.
- ❓ **Icons** — the spec names `Icons.Slice`, `Icons.Projector`, `Icons.RoundingTopLeft`, `Icons.RoundingSW`,
  `Icons.ArrowRight`. **`Icons.Slice` does NOT exist yet** (grep). The others need verifying; new glyphs must
  be added to `Icons.cs` + the icon-font atlas (the `t3-icons*.png` changes already in the working tree).

**Undo (per the global policy):** structural edits need commands that don't exist yet — add/remove/rename/
reorder Surface·Region·Slice·Output·ReferenceImage·Prop, and re-parenting on drag. Only `ChangeSourceQuad`/
`ChangeOutputMappingQuad` (quad drags) exist today. Model everything except selection as an undoable command.

---

## 4. Decisions — **all settled** (2026-07-21, №1 re-revised 2026-07-27)

1. **Content and routing are SETUP-side** (re-revised 2026-07-27, reversing the 07-22 op-side call — see
   §2.1 for rationale and accepted costs): `Setup.ContentSources` + `Setup.Slices`; `Surface.SliceId` /
   `OutputDefinition.SliceId` bind targets; sources key on the op's `SymbolChildId`; `ContentSourceSync`
   keeps the 1:1 op↔source invariant. ✅ (§2.1/§2.4)
2. **Surface tree = flat list + `ParentId`** (nested via parent pointers). ✅ (§2.2)
3. **Physical vs Layout is an explicit `Kind` field**, chosen at creation (callout 22) — not inferred from a
   pose. ✅ (§2.2)
4. **`SendToOutput = { Texture, TargetId:InputSlot<Guid>, SourceRect, Color }`** — one send-op per target
   (model a); content op-side, resolves against `ActiveSetup` (like `UseProjectorCam`). **No back-compat
   migration on this branch.** ✅ (§2.4)
5. **Selection = two planes (entity / sub-element), one address form; not deferred** → [`selection.md`](selection.md). ✅ (§2.6)

Remaining low-stakes sub-choices (decide while building, not blocking): mapping storage surface-side vs join
table (§2.3); Prop templates (§2.8); reference-image m-canvas placement (§2.9).
