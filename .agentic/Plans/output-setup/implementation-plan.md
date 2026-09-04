# Output Setup — Implementation Plan (⚠️ HISTORICAL — phases 1–3b, the sidebar build)

> **⚠️ Status (2026-07-29): this plan's phases are done or superseded.** It records how the branch's
> as-built state came to be (phases 1–3b landed). The *current* plan is
> [`ui-restructuring-plan.md`](ui-restructuring-plan.md) (UI) plus
> [`refactoring-plan.md`](refactoring-plan.md) (debt, with the live progress log). Kept for the
> "Already built" baseline inventory and the deviation notes, which explain present-day code shapes.

Dependency-ordered phases for building the [output-settings spec](output-settings-spec.md). Each phase is
**shippable** (build green, app runs, something works) and **testable**. This is a **migration**, not a
greenfield build — a v1 of most pieces already exists on this branch and gets reshaped, not rewritten from
zero. Grounded in `data-model.md` (the deltas) and the current `Core/Output/` + `Editor/Gui/Windows/OutputSetup/`
code.

## Status
- **Phase 3b (part 3) — out-gutter icons landed (uncommitted), builds green.** Using `Icon.Grid` (surface),
  `Icon.Projector` (output). CONTENT rows show `→ [grid] Surface` / `[projector] Output` / `unbound`; SURFACE
  rows show `[projector] Output +N`. Left *in*-gutter (content-in on surfaces, surface-in on outputs) still to
  come — it needs the left-margin layout. Per-row state matrix (states.md) also still open.
- **Phase 3b (part 2) — drag-to-map landed (uncommitted), builds green** (Editor + Lib). Drag a **surface onto
  an output** → adds a default corner-pin mapping; drag a **content send onto a surface or output** → retargets
  it (new `IOutputSink.SetTarget` → `TargetId.SetTypedInputValue`, which persists). Uses the shared
  `DragAndDropHandling` helper (added a `SetupEntity` drag type) so the orange drop indicator shows on valid
  targets only. **Caveat:** neither drop is undoable yet (structural-undo is still the standing gap); retarget
  persists on the op but has no undo command. Not runtime-tested — drag mechanics built blind.
- **Phase 3b (part 1) — hover cross-highlight landed (uncommitted), builds green.** Hovering a row outlines the
  rows it references (blue `StatusAutomated`) along content→surface→output (+ reference-image→its surfaces),
  one-frame-lagged. **Icon gutters are blocked**: `Icon.Slice`/`Projector`/`Link`/`Referenced` exist, but the
  gutter grammar needs a **surface/grid glyph** for the CONTENT→surface and OUTPUT←surface indicators — needs
  adding to `Icons.cs` before those gutters can be drawn (per the "don't fake glyphs" rule). Drag-to-map + per-row
  state rendering still to come.
- **Phase 3a — sidebar information-architecture reshape landed (uncommitted), builds green.** Five sections in
  the target order (CONTENT / SURFACES / OUTPUTS / REFERENCE IMAGES / PROPS); new **CONTENT** section lists the
  live `SendToOutput` sinks by op name with their target; **surfaces are their own tree** (nested by `ParentId`),
  un-nested from outputs; relationships shown as text (`→ Output`) until the icon gutters land. Content rows are
  selectable (ContentSource kind) with a matching entity card. **Deferred to 3b:** icon gutters, hover
  cross-highlight (Referenced state), drag-to-map, per-row state rendering, region-editing. Panel add/remove/
  rename preserved; mapping a surface to an output still happens in the output view's `+ surface` buttons.
- **Phase 2 — foundation landed (uncommitted), builds green.** `SelectionTarget` + `SubPart` address form;
  `SetupEntitySelection` is now ordered **multi-select** (Select/Add/Toggle/IsSelected/Clear/TryResolve over
  targets) with `Slice`/`ContentSource` kinds added; panel rows are ctrl/shift modifier-aware. The
  **sub-element `CanvasSelection` plane is deferred to Phase 4** (built where the canvas handles consume it,
  rather than as an unused class now). Entity-plane multi-select works; multi-*actions* (delete/drag) come with P3.
- **Phase 1 — landed (uncommitted), builds green** (Editor + Operators/Lib). Model reshaped to the settled
  shapes; corner-pin render path preserved. Deviations from the phase sketch, all deliberate:
  - `Warp?`/`Mask?`/`CornerColors?` on `OutputMapping` were **deferred to Phase 5** (no back-compat cost on
    this branch, so no reason to add dormant fields early). `OutputMapping` is now just `{ OutputId, Mode, Quad }`.
  - `SendToOutput` **reuses the old `SurfaceRef` GUID for `TargetId`**, so existing surface-targeted sends in
    user projects keep their target across the change; the dropped `OutputRef` GUID is ignored on load.
  - `SetupOutputView`'s Content canvas lost its per-surface source-quad editing (that data moved op-side); it's
    now a read-only content preview until op-side slice editing arrives (P3/P4). `ChangeSourceQuadCommand` deleted.
  - Runtime render/round-trip **not yet user-verified** — compiles, logic preserved; needs a real project test.

## Already built (the baseline to reshape)
- **Model:** `Setup, Surface, OutputDefinition(+Camera), ReferenceImage, Prop, DeviceBinding/MachineConfig,
  ActiveSetup, CalibrationPoint, Homography, ProjectorSolver, Pose, Projection`.
- **Ops:** `SendToOutput` (sink) + `OutputSinkRegistry`/`IOutputSink`; `UseProjectorCam`.
- **Editor:** `OutputManager` v1 (surface fan-out composite + full-frame present), `SetupPanel`
  (outputs→surfaces), `SetupOutputView` (corner-pin canvas), `ReferenceImageView` (trace + straighten),
  `OutputSetupModeView` (focus routing). `CanvasEditing/`: `CanvasPointHandle`, `CornerPinHandles`,
  `ICanvasProjection`, `ScalableCanvasProjection`, `ICanvasPointSnapper` (stub). Corner-pin shader + straighten.

So: the **corner-pin authoring loop and the present path already work** — the plan reshapes them to the settled
model and grows the sidebar/canvas/calibration around them.

---

## Phase 1 — Model reshape + keep-green  *(unblocks everything)*
- **Goal:** land the settled model; the build stays green; the existing single-surface corner-pin → output
  render still works through the new shapes.
- **Touches:** `Surface` (+`ParentId`, +`Kind{Physical|Layout}`; `OutputMapping` −`SourceQuad`,
  +`Warp?`/`Mask?`/`CornerColors?` as **dormant data**, unrendered yet). `SendToOutput` →
  `{ Texture, TargetId:InputSlot<Guid>, SourceRect, Color }` (drop `OutputRef`/`SurfaceRef`). `ActiveSetup`
  +`TryResolveTarget(Guid) → Surface|Region|Output`. Fix consumers to compile+run: `OutputManager`,
  `SetupOutputView`, `SetupPanel`, `UseProjectorCam`. Serialization.
- **Δ from v1:** `SourceQuad` → op `SourceRect`; `OutputRef`/`SurfaceRef` → one polymorphic `TargetId`;
  `Surface` gains tree + `Kind`. (Note: in model (a) a "Slice" *is* a `SendToOutput` — no separate class.)
- **Ship when:** app builds; `[Content] → [SendToOutput(TargetId = a surface)]` renders on its output; a setup
  round-trips through JSON.
- **Deps:** none. **Decision to close:** mapping storage — start **surface-side** (`Surface.OutputMappings[]`),
  revisit a join table only if the by-Output view demands it.

## Phase 2 — Selection foundation (two planes)  *(thin, unblocks 3 & 4)*
- **Goal:** the selection model both UI phases consume — build its address form + plane split first
  (`selection.md`).
- **Touches:** `SetupEntitySelection` → **ordered multi** + `Slice`/`ContentSource` kinds (entity plane); new
  `CanvasSelection` (sub-element plane); shared `SelectionTarget`; `CanvasPointHandle` reports its target +
  renders a selected state.
- **Ship when:** multi-select entities (even in the old panel); a corner shows selected; box-select stubbed.
- **Deps:** Phase 1.

## Phase 3 — Sidebar rewrite (the mockup)
- **Goal:** the `CONTENT / SURFACES / OUTPUTS / REFERENCE IMAGES / PROPS` panel from the spec + `states.md`.
- **Touches:** rewrite `SetupPanel`; factor a **reusable tree-row** (disclosure, in/out gutters, state tokens);
  cross-highlight (Referenced state on hover/select); context + add menus (`CustomComponents` app-menu style);
  entity drag-drop; surface tree (`ParentId`) + regions; CONTENT = live sinks grouped by shared texture.
- **Ship when:** the mockup panel is functional — sections, nesting, gutters, hover cross-highlight,
  add/remove/rename (undo), drag a slice onto a surface.
- **Deps:** Phase 1 (tree/Kind), Phase 2 (multi-select + cross-highlight), `states.md` tokens.

## Phase 4 — 2D canvas editing  *(can overlap Phase 3)*
- **Goal:** Figma-like editing of surfaces/slices/regions on the 2D canvas. ~90 % `ImGui.DrawList`.
- **Touches:** tool state + toolbar (Select/Move + create tools); rulers/units/grid overlay (unit from `Kind`);
  real `ICanvasPointSnapper` on `Snapping/SnapResult` + `IValueSnapAttractor`; new handle composers — **edge**
  (crop / `Ctrl`-parallelogram) and **scale/rotate gizmos**; fence-select (sub-element plane); per-type visual
  language + center labels (state tokens).
- **Ship when:** select/warp corners, edge-crop, scale/rotate, snap-with-guides, rulers, and marquee all work
  on surfaces and slices.
- **Deps:** Phase 1, Phase 2. **Decisions to close:** edge modifier map (a/b), tool shortcuts, default snap
  attractors.

## Phase 5 — Rich-mapping render  *(isolated, low-risk, slot anytime after Phase 1)*
- **Goal:** render the warp / mask / color the model already stores.
- **Touches:** `OutputManager` + shader — warp = indexed mesh (~40×40) with **precomputed displacement + recolor
  map**; mask composite; per-corner color (barycentric); soft-edge blend across overlapping mappings.
- **Ship when:** a surface renders lattice-warped + masked + corner-colored; two mappings blend in their overlap.
- **Deps:** Phase 1 only. **Explicitly isolated** — a contained mesh draw over the proven transform; doesn't gate
  the model or the UI, so it can lag the authoring work without blocking it.

## Phase 6 — Calibration, reference workflow, 3D stage  *(largest; likely its own sub-plan)*
- **Goal:** the L2/L3 ladder — much is partially built.
- **Touches:** reference-image **m-canvas** (multi-image placement — new, §2.9 gap); **annotation lines**
  (`AnnotationLineHandles` — unbuilt) + **Apply Lengths**; surface tracing/straighten (built → adapt);
  calibration place+solve (`ProjectorSolver` built → adapt to tools/selection); **3D stage viewer + pose
  transitions** (prototype port — new).
- **Ship when:** trace from a photo → measure → derive geometry → place in 3D → solve a projector → seamless
  photo↔stage transition.
- **Deps:** Phases 1–4.

---

## Sidebar interaction backlog (post-panel polish)
Captured from testing — not yet built:
1. **Reorder / re-parent surfaces by dragging** in the SURFACES tree (drag a row to change order and `ParentId`).
2. **Reorder CONTENT and OUTPUT rows** by dragging (content is op-order; outputs are setup order).
3. **Blink the hover outline** on sidebar rows (animated, not a static outline) for the cross-highlight.
4. **Blink the hovered shape outline on the canvas** too (mirror the sidebar hover on the corner-pin quad).
5. **Drag timing threshold** — require a short hold before a cross-section drop reorders (so dragging a CONTENT
   row toward SURFACES to *retarget* isn't misread as a *reorder*). Distinguish reorder vs. drop-on-target by dwell.

## Suggested first cut
After **Phase 1**, don't fully flesh 3 & 4 — build a **thin vertical slice**: minimal sidebar (list + select)
+ minimal canvas (corner drag only) for **one surface, one send, one output**. It validates the whole
model→panel→canvas→render loop early and surfaces integration friction before you invest in gutters, snapping,
and the tree. Then widen 3 and 4.

## Decisions that gate a phase
| Decision | Gates | Default / lean |
|---|---|---|
| Mapping storage (surface-side vs join table) | P1 | surface-side |
| Edge modifier map (a two-mode vs b three-mode) | P4 | (a) plain=crop / `Ctrl`=parallelogram |
| Tool shortcuts | P4 | pick vs global editor keys |
| Snap attractors on by default | P4 | points + edges + grid |
| 3D left-drag migration (global TiXL nav) | P6 (3D select) | assumed external; click-only until then |
| Prop templates / json | P6 (props) | defer |

## Not in these docs (deliberately out of scope here)
- Player/runtime-side setup loading (editor-first).
- Multi-machine sync beyond `DeviceBinding`.
- The `output-settings-spec.md` callout cleanup (trailing `...`) — working notes, not a blocker.
