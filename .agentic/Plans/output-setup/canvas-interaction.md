# Output Setup — Canvas Interaction

How the on-canvas editing works: selection, direct manipulation, tools, snapping, rulers. Companion to
[`selection.md`](selection.md) (the selection *model*) and [`states.md`](states.md) (row/label *states*).
This doc is the *canvas* behavior.

**North star: feel close to Figma.** Click-select, marquee on empty drag, bounding-box transform handles,
live snapping with guides, rulers, modal tools with keyboard shortcuts, `space`-drag to pan, scroll to zoom.
When a detail is unspecified below, "what would Figma do" is the tie-breaker.

---

## 1. Two canvas contexts — and the left-drag question

The window shows one of two kinds of canvas at a time (focus-driven, per [[project_output_element_mode]] —
no explicit toggle):

- **2D canvas** — output canvas, content canvas, reference-image / straighten canvas. Flat, `ScalableCanvas`
  pan/zoom, an `ICanvasProjection` seam.
- **3D stage** — the world/stage viewer (surfaces placed in 3D, projector cameras, pose transitions).

**The conflict you flagged:** left-drag means *fence-select / create* in a 2D editor, but *orbit* in the 3D
stage (current TiXL). Resolution:

- In a **2D canvas**, **left-drag belongs to the tool** (fence-select or create — §3). Pan = `space`+drag or
  middle-drag; zoom = scroll. Left-drag is *never* pan here (Figma parity), so it's free for editing.
- In the **3D stage**, left-drag stays **orbit** for now.
- **Dependency (not solved by this feature):** you noted TiXL's 3D nav "should change eventually anyway."
  For left-drag to mean *select* in 3D too, TiXL's global orbit-on-left-drag must migrate (e.g. to
  right/alt-drag or an explicit nav tool). Record it as an assumption; don't block the 2D work on it. Until
  then, 3D-stage selection is click-only (no fence), which is acceptable.

---

## 2. Tools (modal, Figma-style)

A per-2D-canvas **active tool**, with a small toolbar + keyboard shortcuts, and `space` as spring-loaded pan.

| Tool | Key | Left-click | Left-drag | Creates |
|---|---|---|---|---|
| **Select/Move** (default) | `V` | select item / sub-element | move selection, or **fence-select** on empty | — |
| **Annotation line** | (tbd) | — | drag out a line | `LineAnnotation` |
| **Calibration point** | (tbd) | drop a point | — | `CalibrationPoint` |
| **Shape / bezier** *(later)* | — | — | — | bezier shape |

- Tools are **modal** (selected tool persists) with **spring-loaded** override (hold a key to temporarily use
  another), matching Figma. After a create, snap back to Select (Figma "create then edit").
- Creating an entity **selects it and focus-renames** it (consistent with spec callout 19.2).
- Which tools appear depends on the canvas (calibration-point tool only in a projector's calibrate canvas,
  annotation tools on the reference canvas, etc.).

---

## 3. Selection on canvas

Uses the two planes from [`selection.md`](selection.md) — **entity plane** (whole items) and **sub-element
plane** (handles). Click targets:

| Click | Plane | Target |
|---|---|---|
| item body / center label | entity | the Surface/Slice/Image/… |
| **corner** | sub-element | `Corner`, index 0..3 |
| annotation endpoint | sub-element | `Annotation`, index |
| empty left-drag | sub-element | **fence-select**: add every handle inside the rect |

**Three kinds of handle — keep them distinct:**
- **Corners** — *selectable and draggable*. Free warp (one point moves), and they carry data (position,
  per-corner color), which is why they're the only selectable sub-element.
- **Edges** — *drag-only, not selectable* (§Edge dragging). A midpoint handle; no selection state.
- **Scale (L/R + corner) and Rotate** — *gizmos drawn around the selection's bounding box* (à la Figma / the
  prototype's L-R handle). Rigid transform of the whole selection; appear only once something is selected;
  **not** selectable — rendered *from* the selection, never *in* it.

So: drag a **corner** → free warp; drag an **edge** → crop/parallelogram (below); drag a **scale gizmo** →
rigid resize; drag **rotate** → rotate; drag the **body** → translate.

### Edge dragging

Hard-won from the HTML prototype; the crop-vs-scale distinction is subtle, so it's spelled out:
- **Handle only** — edge dragging starts at a **midpoint edge handle**, not anywhere along the edge. No
  selection state.
- **Plain drag = crop** — the edge moves along its **normal**, opposite edge fixed. **Straight mode keeps it
  axis-aligned.** Context-dependent write, and this is the trap: for a **Slice** it adjusts the `SourceRect`
  on that side (reveal/hide *source* pixels); for a **Surface** it changes the *extent*. Looks identical,
  writes different data.
- **`Ctrl` drag = free edge / parallelogram** *(provisional — modifier map TBD)* — the edge translates by the
  full drag delta (both endpoints together): perpendicular *extend* **and** parallel *slide* at once → a
  parallelogram/shear. Plain = constrained (perpendicular, straight); `Ctrl` = unconstrained.
  - *Alt reading not chosen:* `Ctrl` = pure proportional scale (opposite edge anchored) with parallelogram as
    a separate `Alt`/`Shift` mode. Leaning against — one modifier, parallelogram as "just unconstrained drag".
- **Content cards have no edge handles in v1 (2026-08-29):** on the board, a content card's corners
  scale its px-per-m *presentation* only (ui-restructuring §C.1); its edges stay reserved, because
  edge-dragging *content* means cutting a slice (UV sub-rect) — that arrives with slice editing, not
  v1. Keeps the grammar to one meaning per handle type: **corners = scale, edges = crop/slice**.
- **Synchronized crop on content-bearing surfaces/regions (2026-08-31):** a plain edge drag co-edits
  the rect *and* the slice UV — **the pixels on the wall stay put**, the window over them shrinks
  (Figma-frame feel). This completes "a crop never moves anything on the wall" for content, which
  the raster and annotations already obey. The *stretch* variant (extent changes, content re-fits)
  moves to the scale gizmo / a modifier, where "I'm distorting" is explicit.
  - **Pan:** modifier + body-drag inside a region slides the slice UV under the fixed window — same
    invariant (slice fills region), no extra state.
  - **Shared slices fork on crop (copy-on-write):** cropping a slice that feeds other
    surfaces/outputs forks a private slice for this surface — local intent wins; the shared edit
    stays reachable on the content card, where shared-ness is visible.

---

## 4. Direct manipulation → what each edit writes

Every drag is one undoable command (undo policy: all but selection). Reuse `CanvasEditing/CanvasPointHandle`
for point handles; add edge + gizmo handles as new composers next to `CornerPinHandles`.

| Gesture | Writes | Notes |
|---|---|---|
| move body | entity position (Layout px / Physical m) | translate |
| drag corner | the relevant quad corner (`OutputMapping.Quad`, `Slice.SourceRect`, `ReferenceBinding.Quad`) | per-context; free warp |
| drag edge (plain) | that edge's two corners along the normal | **crop**; straight mode axis-aligns — see §Edge dragging |
| drag edge (plain, content-bearing surface/region) | region rect **and** slice UV together | **synchronized crop** — wall pixels stay put (§Edge dragging) |
| modifier + body-drag inside a region | slice UV only | **pan** the source under the fixed window |
| drag edge (`Ctrl`) | that edge's two corners by the full delta | **parallelogram/shear** *(provisional)* |
| drag patch rect / corners | `OutputDefinition.Patches[i].Quad` | axis-aligned tile by default; warped quad = surface-less keystone (data-model §2.5) |
| drag ghost-frame corner (board, mapped surface) | `OutputMapping.Quad` | inverse-side edit of the same quad as the Output camera (ui-restructuring §C.2b) |
| scale gizmo | uniform/again-axis scale about the pivot | `StagePlacement.Pivot` is the anchor |
| rotate gizmo | orientation about pivot | Physical: the pose; Layout: 2D angle |

Which quad a corner-drag writes depends on the canvas (output canvas → mapping quad; content canvas → slice
rect; reference canvas → trace quad) — the canvas owns that binding, the handle stays generic.

**Patch editing (2026-08-31):** patches drag/snap as rects *inside the output card* (px rulers,
tile-to-tile + grid snapping); "Split 2×2 / 4×4" context actions seed matrix layouts. **Warp**
attaches in the quad context — an "Add warp" context action on the selected patch/mapping quad
(modifier stack, ui-restructuring §A.3) — never a global tool.

**Rotation is deferred (2026-08-29):** the corner pin absorbs projective distortion, so the
single-projector flows gain nothing from it, while it complicates every axis-aligned interaction
(edge crop, snapping, floor alignment). The data model keeps `Pose.Orientation`. When a real flow
pulls it in, the dominant case is **90° steps** (portrait surfaces) — a rotation-field property with
step buttons (ui-restructuring §A.3) before any gizmo. Mind the entity split: a portrait-mounted
*projector* is output-level rotation of the mapping; a portrait *surface/banner* is stage-space
surface rotation.

---

## 5. Snapping

Build on the existing per-axis model — `Snapping/SnapResult` + `IValueSnapAttractor` (same infra the timeline
and MagGraph use) — behind the stubbed `ICanvasPointSnapper` seam. Snap **one axis at a time**, draw a guide
on a hit.

Candidate attractors:
- **other points** (corners of other items, annotation endpoints)
- **other edges** (extend an edge, align to it)
- **grid lines** (§6) and **ruler guides** (dragged from the rulers, Figma-style)
- **the item's own axes** for symmetry

**Angle constraints:** `shift` constrains a drag to **H / V / 45°** (Figma). In **straight mode** (the
reference straighten view) this is the common case, so it may default **on** there — a rectified surface wants
axis-aligned edges. Constraint math is angle-snapping on the drag *vector*, independent of point snapping
(both can apply).

---

## 6. Rulers, units, grid

- **Rulers** on the top/left gutters, ticks at nice intervals. **Unit follows the surface `Kind`** (data-model
  §2.2): **meters** for `Physical`, **pixels/rem** for `Layout`. Reference-image canvas is in **meters**
  (spec callout 25.4 — assemble plans/photos to scale).
- **Grid** tied to the unit (e.g. 0.1 m / 1 m majors), drawn like the existing `MagGraphCanvas`/`GraphView`
  background grids but labelled. **Snap-to-grid** optional, feeding §5 as one more attractor.
- Rulers/grid are **new** on `ScalableCanvas` — it gives the transform (`TransformPositionFloat` etc.); the
  ruler/grid renderer is a new overlay reading the current scope + unit.

---

## 7. Visual language per entity type

One consistent scheme so Images / Slices / Surfaces / Outputs / annotations are distinguishable at a glance,
**matching the sidebar** icons+colors ([`states.md`](states.md)) and the in/out gutter grammar:

| Type | On-canvas form |
|---|---|
| Reference image | the photo/plan bitmap itself |
| Slice | dashed sub-rect on the *source image* (it's a source cut) |
| Patch | labeled sub-rect inside its *output card* (a canvas cut — the mirror of Slice) |
| Surface | filled quad + faint grid, entity color; corners as handles |
| Region (Layout child) | nested rect inside its surface, lighter |
| Output | its frame outline (rare on the 2D canvas; mostly the 3D stage) |
| Annotation line | colored line + endpoints (green = guide, yellow = measurement, per spec) |
| Calibration point | cross/dot marker with residual tint |

**Center label** — each item shows its **name at its center**, styled by the same state tokens as the sidebar
rows (Default / Hover / Selected / Referenced / …). It is the on-canvas twin of the tree row, so selection and
cross-highlight read identically in both places.

**Container & handle grammar (2026-08-31, from the board sketches):**
- **Outer strokes for containers, inner outlines for children** — a card's border is a stroke; its
  sub-rects (slices, patches, regions) are thinner inner outlines. Nesting reads at a glance.
- **Handle shapes carry meaning:** **round handles = projection/perspective** (mapping and patch
  quads, calibration); **square handles = axis-aligned crop/scale**. Never mixed on one object.
- **Handles tint by the entity's type color** (`states.md` tokens) — the handle itself says what it
  edits.
- **Frame captions show the selection's nesting path** (`Send1 › Slice 2`) while a child is
  selected — the caption is the breadcrumb's on-canvas twin.
- **Slice badge on cropped regions (2026-08-31):** once a region's slice is no longer full-frame it
  carries a small slice indicator; hovering cross-highlights the dashed slice rect on the content
  card — "this is a window onto that" without opening anything. Distinct from dashed *derivation
  wires* (jig): a crop is direct editing, no link semantics.

---

## 8. Open decisions & dependencies

1. **3D left-drag migration** (§1) — a global TiXL nav change; assumed, not owned here. Until it lands, 3D
   stage = click-select only.
2. **Tool shortcuts** — pick keys once the tool set firms up (avoid clashing with global editor shortcuts).
3. **Edge-drag modifier map** (§Edge dragging) — confirm **(a)** plain=crop / `Ctrl`=free-parallelogram
   *(leaning)* vs **(b)** plain=crop / `Ctrl`=proportional-scale / `Alt`=parallelogram. Edges stay
   *non-selectable* either way.
4. **Gizmo set** — confirm scale is corner+side (8-handle) or L/R only (prototype). Rotate: outside-corner
   hover ring (Figma) vs a dedicated handle.
5. **Snap toggles** — which attractors are on by default, and the modifier to suspend snapping (Figma: hold a
   key).
6. **Inner-content selection & handle exposure on cards** (2026-08-31 train notes) — how a card
   reveals its children's handles: click-again deep select (Figma; leaning), hotkey reveal
   (`Alt`/`Ctrl`), or an explicit action. Against always-on handles: they expose inner content,
   collide with card resizing, and distortion is irrelevant for most use-cases. Same decision as
   ui-restructuring open question 7 — settle once, apply to both.

## 9. Code map
- `Interaction/CanvasEditing/`: `CanvasPointHandle` (points), `CornerPinHandles` (quad). **New composers:**
  edge handles, transform gizmos (scale/rotate bbox), annotation-line handles, calibration-point handles.
- `ICanvasProjection` / `ScalableCanvasProjection` — the backend-neutral transform seam (works for the future
  2D-camera views too).
- `ICanvasPointSnapper` → real impl on `Snapping/SnapResult` + `IValueSnapAttractor`.
- Rulers/grid overlay — new, reads `ScalableCanvas` scope + the surface `Kind` unit.
- Tool state + toolbar — new, per 2D canvas (owned by `SetupOutputView` / the reference view).
