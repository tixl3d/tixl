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
- **Slice-fills-region invariant (2026-08-31):** a slice always fills the surface/region showing it —
  there is *no* third "image transform inside the container". The pair *(slice rect, region rect)*
  fully determines placement; every crop and pan is a co-edit of those two rects
  (`canvas-interaction.md` §Edge dragging, synchronized crop). A surface is thereby a Figma-frame-like
  container *behaviorally*, without a second parameterization that would break slice sharing and the
  source-side view. **Copy-on-write:** cropping a slice with other consumers forks a private slice —
  shared edits happen on the content card, where shared-ness is visible.

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
  - `Mask`: optional (soft-edge feather / arbitrary), with a **space tag** (2026-07-29):
    *surface-space* masks for blend ramps — the band is authored once in wall coordinates and shared
    (paired) across the two mappings so the ramps sum to unity on the wall regardless of per-projector
    pixel density/keystone; *output-space* masks for frustum-relative uses (spill blocking). Blend
    profiles are gamma-aware (sum in light, not signal; adjustable, ~2.2 default).
    **Output-level masks too (2026-07-29):** a whole-canvas mask lives on the `OutputDefinition`
    (same category as whole-canvas trim) — needed so the direct content→output pipe can be masked
    without materializing a surface/mapping ("ceiling spill" on a flow-1 setup).
    **Mask payload (2026-08-31):** single-channel alpha + tint color — or better an **SDF**, so
    feather/blur becomes a cheap distance offset instead of a blur pass.
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

### 2.5 Direct-to-output (surfaceless pipe) — 🟡 half-present · **REVISED 2026-07-29: transport lives on the binding, not the output**
The full-frame path exists in `OutputManager` (`_fullscreenNdc`), and `OutputDefinition.Kind` already
distinguishes `Default/Format/Projector/Display`.

<details><summary>Superseded: NDI/Spout as output kinds</summary>
Earlier direction: add **`NDI`/`Spout` kinds** (spec callout 24) with the model-level statement that
*these kinds are direct-only* (no surfaces); target-path picker filters by kind.
</details>

**Decision (2026-07-29, from the overview sketch):** an Output is a purely **logical pixel canvas**;
the *transport* (physical connector, Spout stream, NDI sender) is a **binding kind** in the per-machine
file — `Spout "Spout1"` sits next to `Display 2` as a peer binding target. Consequences:
- `OutputDefinition.Kind` loses transport meaning (reduces to canvas semantics); no NDI/Spout kinds.
- Any output can carry mappings regardless of transport (a Spout output composites surfaces like a
  projector); "direct-only" stops being a kind property.
- Transport is machine-scoped automatically — venue-portable setups, per-machine plugs (what
  multi-machine needs; see `multi-machine.md` §4).
- **Outputs are auto-created and visually merged when trivial** ("keep the entity, kill its
  visibility"): binding content/a surface to a plug materializes the output silently, display-named
  after its binding (`@ Display 2`) until renamed; the outliner shows one merged pill while
  output↔binding is 1:1 and uncalibrated, and splits the columns only when compositing, calibration,
  an unbound output, or a second machine forces the distinction. Default names are role-based, never
  transport-copies (no `Spout 1 → Spout 1`).
- **Resolution follows the TiXL-wide `0,0` auto convention, resolved backwards**: default
  `CanvasResolution = 0,0` resolves from the bound display's mode; slices/sources at auto resolve from
  their downstream consumers (**max over consumers** on fan-out, rendered once); the resolved value
  reaches the op graph as the context's requested resolution (all-auto + 4K display ⇒ ops render 4K).
  Unbound + all-auto falls back to the project default. UI: auto shows the resolved value plainly
  (muted); a lock icon marks an active override; typing pins the stage, clearing re-links.
- **Quad storage consequence:** `OutputMapping.Quad` is authored in output px, and auto resolution
  makes the canvas size changeable at bind time. Store the **authored canvas resolution alongside the
  quads** and rescale proportionally when the resolved resolution changes at the same aspect; on an
  *aspect* change don't silently rescale — the physical optics changed, so flag the mapping as stale
  calibration (readiness surface) instead. (Cheapest now — "no migration on this branch" still holds.)

**Patches — the direct pipe grows plural (2026-08-31):** the single `Output.SliceId` full-frame pipe
generalizes to **`OutputDefinition.Patches[] = { SliceId, Quad, Name }`** — N canvas regions, each
fed by one source slice. One concept covers the whole surface-less ladder:
- **Rung 0** — full-frame pipe = one implicit full-canvas patch (as built).
- **Rung 0.5** — *keystone without a surface*: one patch with a warped quad ("how the image is
  projected within the output"). Serves the sofa-projector case with zero new concepts.
- **Packing** — N axis-aligned patches tiling the canvas (TV wall / 4×4 split matrix / LED
  processor). "Split 2×2 / 4×4" helpers + matrix presets make it a 30-second job.
- **Promotion** — when a surface-only feature is reached for (real size, raster, straighten,
  compositing *in meters*), "Use on Surface" materializes the surface and the patch quad transfers
  **verbatim** to `OutputMapping.Quad` — same numbers, nothing moves on the wall, no convert moment.
- **One home at a time (hard rule):** a route's quad lives on the patch **or** on a mapping, never
  both — promotion clears the patch. Renderer and file readers treat them as mutually exclusive.
- **Aspect policy:** slice↔patch mismatch defaults to **stretch** + a readiness hint (same pattern
  as the corner-pin aspect guard). **Overlap allowed** (free PIP); painter's order = list order;
  reorder UI deferred.
- **The boundary, restated:** *patches model the canvas (pixels); surfaces model the room (meters,
  poses, calibration).* Pixel-space compositing never needs a surface; real-world geometry always
  does. (Replaces the earlier "compositing forces surfaces" instinct.)
- Naming: **Patch** (AV patching vocabulary; *Tile* recorded as the alternative). Never "slice" —
  one name per end of the pipe.

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

### 2.7 Output ↔ machine display binding — ✅ model ready, 🟡 UI missing · **virtual displays added 2026-07-29**
The display list gains **virtual displays** that always exist (`Editor Display` = under the editor's
top-left corner; `2nd Display` = first non-editor display, per-machine overridable) — the 4.2
"Default Audio Input" pattern applied verbatim: no new binding kind, a binding still points at a
display; virtual names resolve in machine-wide settings before OS enumeration. Examples ship
runnable. Details + resolution-timing rules in [`binding-examples.md`](binding-examples.md).
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

### 2.10 Reference jig — derived slices from a traced photo (2026-08-31)

For arrangements whose slice rects encode physical reality (TV wall behind a split matrix): the
middle construct is a **Surface with `Render` off**, reused as a pure derivation jig — straightened
reference photo as backdrop, screens traced as Layout regions, all existing machinery. What it adds
over eyeballing sixteen UV rects: **bezel-correct gaps** (skipped source pixels — what makes the
image read as continuous), true per-screen aspect + proportional scale, absolute scale via
`MetersPerPixel`, re-editability (retrace → regenerate), venue portability (re-photograph at the
next venue; routing and patches survive).

- **Content footprint** — a rect in jig space stating how the source image spans the arrangement;
  the one authoring decision a photo can't make. Default: a content-aspect rect fitted around the
  traced bounding box. Derivation is a rect transform:
  `sliceUV = (regionRect − footprint.min) / footprint.size` (clip + readiness hint outside).
- **"Generate slices from regions"** — one undoable command; derived slices carry a link badge; an
  explicit "Update slices from regions" re-derives (live sync only if regeneration ever itches).
  Direct edits on a derived slice detach the link.
- **What the jig cannot derive: patches.** Matrix wiring is cabling, not geometry — slice→patch
  routing stays manual, made fast by **Identify** (click a patch → its screen shows a number).
- On the board the jig sits in the flow as an **authoring dependency, not a render step** — dashed
  derivation wires; the render path (slice → patch) stays solid. If the venue later swaps the matrix
  for a projector: flip `Render` on, add a mapping — the jig is already a traced surface (the
  ladder again).

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

## 4. Decisions — **all settled** (2026-07-21, №1 re-revised 2026-07-27, №6–7 added 2026-08-29, №8–9 added 2026-08-31)

1. **Content and routing are SETUP-side** (re-revised 2026-07-27, reversing the 07-22 op-side call — see
   §2.1 for rationale and accepted costs): `Setup.ContentSources` + `Setup.Slices`; `Surface.SliceId` /
   `OutputDefinition.SliceId` bind targets; sources key on the op's `SymbolChildId`; `ContentSourceSync`
   keeps the 1:1 op↔source invariant. ✅ (§2.1/§2.4)
2. **Surface tree = flat list + `ParentId`** (nested via parent pointers). ✅ (§2.2)
3. **Physical vs Layout is an explicit `Kind` field**, chosen at creation (callout 22) — not inferred from a
   pose. ✅ (§2.2)
4. **`SendToOutput = { Texture, Update, Color }`** — no target param on the op; routing is
   `Surface.SliceId` / `OutputDefinition.SliceId` in the Setup (re-revised 2026-07-27 together with
   decision 1 — the earlier op-side `TargetId` shape is superseded, see §2.4). **No back-compat
   migration on this branch.** ✅ (§2.1/§2.4)
5. **Selection = two planes (entity / sub-element), one address form; not deferred** → [`selection.md`](selection.md). ✅ (§2.6)
6. **Spaces & units are fixed** (2026-08-29): everything measured in **meters is Y-up** (floor at
   y = 0); everything measured in **pixels/UV stays Y-down**. Surface-local space is normalized to
   Y-up in the same pass as the anchor change. ✅ (§5)
7. **Anchors are signed centered** (2026-08-29): range −1..1, center `(0,0)`, Y-up (bottom-center =
   `(0,−1)`); the term is **Anchor** — `Pivot` is deprecated (implies rotation-center only).
   Conversion from the as-built `StagePlacement.Pivot` (unsigned 0..1, bottom-left):
   `pivot01 = (anchor + 1) / 2` with the Y flip; no back-compat migration on this branch. ✅ (§5)
8. **Output-side Patches; pixels-vs-meters boundary** (2026-08-31): the direct pipe generalizes to
   `OutputDefinition.Patches[] = { SliceId, Quad, Name }` — surface-less keystone (one warped patch)
   and output packing (N rects) in one concept. Quad promotion to a mapping is verbatim and
   exclusive (one home at a time). *Patches model the canvas; surfaces model the room.* ✅ (§2.5)
9. **Reference jig pattern** (2026-08-31): physically-derived slice sets come from a `Render`-off
   surface used as a tracing jig (photo backdrop + Layout regions + content footprint) with a
   generate/update command — an authoring dependency, never a render step. ✅ (§2.10)

Remaining low-stakes sub-choices (decide while building, not blocking): mapping storage surface-side vs join
table (§2.3); Prop templates (§2.8); reference-image m-canvas placement (§2.9).

---

## 5. Spaces & units (2026-08-29)

The rule that ends every "which way is Y here": **meters ⇒ Y-up** (physical space — nobody reasons
about a wall upside-down; matches the 3D stage pose and camera conventions), **pixels/UV ⇒ Y-down**
(the universal image convention — fighting it would make every texture interaction a sign bug).

| Space | Unit | Origin | Y | Used for |
|---|---|---|---|---|
| **Board / Stage** | m | floor line = y 0 | **up** | card placement (`CanvasPlacement`), `StagePlacement.Pose`, props, guide lines |
| **Surface-local** | m | surface **anchor** | **up** *(target — see caveat)* | annotations, calibration raster, child-region rects, `LocalPosition` |
| **Slice / texture UV** | 0..1 | top-left | down | `OutputMapping.SourceQuad`, slice rects |
| **Output canvas** | px | top-left | down | `OutputMapping.Quad`, `CanvasResolution`, binding display modes |
| **Reference image** | px (+ `MetersPerPixel`) | top-left | down | `ReferenceBinding.Quad`, trace annotations |

- **Anchors: signed centered, −1..1, Y-up** — center `(0,0)`, bottom-center `(0,−1)`, corners
  `(±1,±1)`. The important cases are round numbers; mirroring is a sign change; the floor-standing
  default composes as anchor `(0,−1)` + position `(x, 0)`. One convention for **all** frame kinds
  (surfaces, content cards, outputs) — no per-kind variants. Term: **Anchor** (decision №7); a
  separate rotation center can become its own property later if a flow ever needs one.
- **Landed 2026-09-05** (inventory and site list in [`anchor-yup-pass.md`](anchor-yup-pass.md)):
  `Surface.Anchor` (signed, default bottom-centre `(0,−1)`) replaced `StagePlacement.Pivot`; surface-local
  space is Y-up with its **origin at the anchor**, so a crop never moves annotations or child regions.
  No legacy conversion — the format was internal-preview only.
