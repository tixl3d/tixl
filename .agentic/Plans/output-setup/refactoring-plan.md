# Output Setup — Code Review & Refactoring Plan (2026-07-27)

In-depth review of the `feat/projection-mapping` branch (vs merge-base `466d28a7`, ~14.5k added lines),
focused on (1) selection-handling generalization, (2) the data model, (3) reusable UI components. Three
independent review passes, findings cross-verified against the code.

## Verdict

Substantially better than "vibe code" fears suggest — but with concentrated, fixable debt:

**Ship-quality already:** serialization discipline (version fields, tolerant readers, string discriminators,
venue/machine/project storage split), the math stack (`Homography`, `ProjectorSolver`, `LineRectifier` — real
tests, correct numerics, Try-pattern + span APIs), theming/scaling (zero `new Color(...)`, consistent
`UiColors` + `Fade()` + `UiScaleFactor`), Guid identity throughout, and the shared row/menu components inside
`SetupPanel` (one `DrawEntityRow` for all sections; sidebar and canvas share menu bodies).

**The debt, in one sentence each:**
1. The code implements a routing model the design doc explicitly reversed (setup-side `ContentSource`/`Slice`
   vs the "settled" op-side `TargetId` decision in `data-model.md` §2.4).
2. The sub-element selection plane (`CanvasSelection`) from `selection.md` was never built; selection state is
   shadowed in four places; the canvas has no multi-select, no modifiers, no fence.
3. Undo covers ~15% of the spec's own "everything but selection" policy (3 commands vs ~30 direct-mutation
   sites), and undo can silently cross venues.
4. Per-frame allocation discipline collapses in the panel (~100+ closure/string/list allocations per frame
   with a populated setup).
5. Two god units: `SetupPanel` (2331 lines, `static` with ~20 mutable static fields under a *per-window*
   caller) and `OutputManager` (826 lines, five jobs).
6. A handful of real correctness bugs (below).

None of it is schema-breaking; the entity model itself needs no migration-forcing changes.

## Revision (2026-07-27): sidebar → flow-view pivot

> **Update (2026-07-29):** the pivot is now fully designed in
> [`ui-restructuring-plan.md`](ui-restructuring-plan.md) (three phases + glossary). Where this section
> speculates ("property cards likely survive as an inspector next to the flow view"), that plan
> decides: cards move to the **Parameter window**, `FormInputsNarrow` is **deleted** (see P4.5 below).
> The extraction guidance below (`SetupActions`, `SetupRelations`, `EntityItem`, shared selection)
> all still holds and is mostly done — see Progress.

After user tests, the tree sidebar is expected to be partially **rewritten as a graph-like flow view**
(source → Slice → Surface → Region → Mapping → Output → Device, connection lines à la MagGraph but custom —
no parameter handling needed). Canvas editing stays essential. This changes what's worth polishing:

**Clarified (2026-07-27): the flow view shows the *same items*, differently laid out and connected.**
So the row work is not dropped — it's re-scoped:
- **Extract a layout-agnostic `EntityItem` widget** from `DrawEntityRow`'s content: icon + label + inline
  rename + state tokens (states.md matrix) + context menu + drag source, drawn *at a given rect* rather
  than assuming tree indentation. The tree draws it in rows today; the flow view draws it in node boxes
  tomorrow. Do the args-struct/no-per-row-delegates redesign **as part of this extraction** — the same
  items on a node canvas have the same (likely worse) per-frame allocation profile.
- What actually dies: the tree-specific *layout* — indentation, disclosure chevrons, the in/out **gutter
  icons** (replaced by real connection lines), and section headers. Don't polish those.
- `FormInputsNarrow` polish beyond the checkbox/StylizedText dedup — the property cards likely survive as an
  inspector next to the flow view, but don't refine the card layout until the new UI shape is known.

**Upgraded (these now *enable* the flow view instead of merely cleaning up):**
- **Extract entity CRUD out of `SetupPanel` into `SetupActions`** — the flow view needs the exact same
  add/delete/duplicate/rebind operations; leaving them inside the doomed panel means porting them under
  time pressure later. This part of P2.1 moves *up*, the rendering split moves *down*.
- **Extract the relationship queries (`ComputeReferenced`, `Describe*Gutter`, the parent-walks) into a
  `SetupRelations` unit** — a flow view is literally a rendering of this relationship graph; it becomes the
  view's data source rather than gutter-icon plumbing.
- **P1 (routing-model decision) now gates the flow view**: the edges you draw *are* the routing model.
  Deciding setup-side vs op-side after building the view means redrawing it.
- **Mapping storage sub-choice resurfaces** (`data-model.md` §2.3 deferred it "until building the view" —
  that's now): a `Setup.Mappings[]` join table `{SurfaceId, OutputId, …}` reads far more naturally as graph
  edges than surface-side lists. Cheap to flip pre-migration.
- **P2.2 (SelectionSet + selection completion) gains a third consumer**: flow-view nodes, canvas entities,
  and sub-element handles should share one selection stack from day one.

**Reuse pointers for the flow view:** `ScalableCanvas` (+ the existing `ICanvasProjection` seam) for
pan/zoom; `MagGraphCanvas.DrawConnection.cs` as the reference for line rendering (copy the patterns, not
the class); `SelectionFence` for marquee; `FrameStats.PulseItemWithId` already carries cross-highlight
between views. Note: Device nodes come from per-machine `DeviceBinding` — the flow view's rightmost column
is machine-specific and should render unbound outputs distinctly.

---

## P0 — Correctness bugs (fix before anything else, each is small)

| # | Bug | Where | Fix |
|---|-----|-------|-----|
| 0.1 | **Stored quads drift through a float round-trip every frame.** In Straight/Content mode every surface's `mappingData.Quad` is rewritten `view → back` per frame even with no drag; homography forward-inverse in float is not identity, and any `SaveActive` persists the drift. | `SetupOutputView.cs:456-458` | Write back only for the surface with an active drag (`phase != None`). |
| 0.2 | **Undo can cross venues.** Commands resolve entity Guids against `ActiveSetup.Current`; `Setup.Duplicate` preserves Guids by design, so Ctrl-Z after a venue switch applies venue A's old values into venue B's file — and `Apply` calls `SaveActive`, persisting it. | `ChangeOutputMappingQuadCommand.cs`, `ResizeSurfaceCommand.cs`, `DeleteSurfaceCommand.cs` | Capture `Setup.Id` in every setup command; no-op + `Log.Warning` when `ActiveSetup.Current.Id` differs. |
| 0.3 | **Deleted slices stay selected forever.** `ExistsInSetup` returns `true` unconditionally for `Slice`/`ContentSource` (stale comment: "live on graph ops, not the setup" — slices are setup data now). Sidebar delete doesn't clear selection; the canvas-menu delete does — two paths disagree. | `SetupEntitySelection.cs:98-108`, `SetupPanel.cs:747` vs `:838-842` | Validate `Slice` against `setup.Slices`, `ContentSource` against `setup.ContentSources`; let `TryResolve` pruning be the single cleanup path. |
| 0.4 | **`ActiveSetup.Current` is published as a side effect of a Try-getter.** 22 call sites; if no UI happens to poll after a project switch, operators resolve the previous project's setup. | `OutputSetupHandling.cs:30-33` | Publish from project focus/open/close events; make `TryGetActiveSetup` a pure read. Evict `_entriesByProjectFolder` on project close (`:230`). |
| 0.5 | **ContentSource↔op sync only runs while the panel is drawn, and writes the setup file from inside a draw frame.** Panel closed ⇒ no adoption/rename/cascade; the claimed 1:1 invariant is UI-frame-gated. | `ContentSourceSync.cs:68-69`, called only from `SetupPanel.cs:48` | Move `ContentSourceSync.Update` to a per-frame (or registry-version-triggered) hook independent of the panel; debounce the save out of the draw path. |
| 0.6 | **Cross-window state bleed.** `SetupPanel` static fields (`_renamingId`, `_hoveredId`, `_primaryKind/_Id`, `_referenced`, …) are shared across all OutputWindows while selection is per-window; two open panels co-rename and cross-highlight each other. | `SetupPanel.cs:2300-2330` | Falls out of P2.1 (instance-ify SetupPanel). Until then it's a latent bug, not a blocker. |
| 0.7 | **Double content evaluation per frame.** Both `UpdatePresentation` and the output view call `OutputManager.RenderOutput` in the same frame; each bumps the global invalidation tick and re-evaluates every sink. | `OutputManager.cs:157-164`, `WindowsUiContentDrawer.cs:153`, `SetupOutputView.cs:129` | Guard with a per-frame tick (`ImGui.GetFrameCount()`), invalidate once. |

Effort: ~1–2 days total.

---

## P1 — Architecture decision: reconcile the routing model
**DECIDED 2026-07-27: setup-side blessed (option a).** `data-model.md` §2.1/§2.4 updated with the rationale
and accepted costs; the superseded op-side decision is kept there for history. Remaining hardening from this
choice: the `SymbolChildId` ambiguity when one composition is instanced twice (first-registered sink wins).

`data-model.md` §2.4/§4 records as **settled (2026-07-22)**: content is op-side, no stored `ContentSource`
entity, `SendToOutput = { Texture, TargetId:InputSlot<Guid>, SourceRect, Color }`. The code does the
opposite: `Setup.ContentSources` + `Setup.Slices` exist (`Setup.cs:32-37`), sources are keyed by
`SymbolChildId` (`ContentSource.cs:21` — exactly the op-id-path keying the doc reversed *because of* the
orphan problem), `Surface.SliceId` points the inverse direction, and `SendToOutput` has no `TargetId`.
The predicted costs are real: duplicating a send-op doesn't carry routing, and orphan cleanup needed a
whole-registry scan subsystem (`ContentSourceSync.DropDeletedSources`).

**Decide now — this is the cheapest it will ever be** ("no migration on this branch" is still in force):

- **(a) Bless the as-built model:** update `data-model.md` with why the 07-22 decision was reversed again,
  and accept the sync subsystem as permanent. Then harden it (P0.5) and fix `SymbolChildId` ambiguity when
  one composition is instanced twice (`OutputManager.cs:609-618` — first-registered wins arbitrarily).
- **(b) Migrate to the op-side model:** several days; deletes `ContentSourceSync`, the orphan scan, and the
  duplication bug class in one move.

Either is defensible; leaving doc and code contradicting each other is not — the next planning session will
build on the wrong one.

---

## P2 — Structural refactoring (do before the next 2D-canvas feature)

### 2.1 De-god `SetupPanel` (~1–2 days)
- **Instance-ify:** make it a per-`OutputSetupModeView` instance (fixes P0.6). `OutputSetupModeView` /
  `ReferenceImageView` already model the correct pattern.
- **Partial-class split** (mechanical, matches `SetupOutputView.Slices/.Measure` convention next door):
  `SetupPanel.Rows.cs` (tree/row rendering), `SetupPanel.Cards.cs` (property cards),
  `SetupPanel.Actions.cs` (entity CRUD — these are model operations, not UI).
- **Break the bidirectional canvas↔panel coupling:** `SetupOutputView` calls `SetupPanel.DuplicateSurface`,
  `DrawSurfaceMenuItems`, `DrawSliceMenuItems`, `SliceLabel`; move shared actions/menus/labels into a neutral
  `SetupActions`/`SetupMenus` unit both consume (~half day). Sharing menu bodies was the right instinct —
  only the home is wrong.
- Relationship queries (`ComputeReferenced`, `Describe*Gutter`, parent-walk duplicated at
  `SetupPanel.cs:440-458` vs `OutputSetupModeView.cs:199-222`) move to one `SetupRelations` helper.

### 2.2 Finish the selection model (~1 week; this is Phase 2/4 of the existing plan, not new scope)
The `SelectionTarget` address form and Guid-based `SetupEntitySelection` are the right foundation — better
than the legacy object-ref `NodeSelection`. What's missing is the second plane and consistency:

- **Extract a generic `SelectionSet`** (ordered targets, Set/Add/Toggle/Clear, primary) into
  `Editor/UiModel/Selection/`; split validation from mutation — `TryResolve` currently prunes as a side
  effect and is called 3–4× per frame with closure allocations (`SetupEntitySelection.cs:84,103-106`).
  Resolve via an interface, not captured lambdas.
- **Build `CanvasSelection` (the sub-element plane)** before more canvas editors appear. Today `SubPart` /
  `Part` / `Index` are dead code, and "which sub-thing is hot" is six parallel ad-hoc field sets
  (`_dragSurfaceId`, `_edgeDragSurfaceId`, `_labelMoveSurfaceId`, `_measureDraftIndex`, …
  `SetupOutputView.cs:1523-1559`, `.Measure.cs:399-400`). Collapse the six drag state machines onto one
  "hot `SelectionTarget` + snapshot + command" skeleton (generalize the existing `RunResizeDrag`).
- **Canvas parity with the tree:** `SelectPicked` always replaces (`SetupOutputView.cs:1306`) — add
  ctrl/shift modifiers, and wire the existing screen-space `SelectionFence` (used by MagGraph/TimeLine,
  drop-in) for marquee on handles. Note: tree shift-click is plain Add, not the planned range-extend.
- **Kill the selection shadows:** `_focusedSurfaceId`, `_selectedSliceId` (synced from two directions),
  and `SetupPanel._primaryKind/_Id` are per-frame copies of the primary; pass the primary down as a
  parameter instead of persisting copies in fields.
- **Don't unify with `NodeSelection` now.** Its object-ref model is load-bearing legacy; convergence runs
  the other way — future Figma-style canvases adopt `SelectionSet` + `CanvasItemPicker` + `SelectionFence` +
  `CanvasPointHandle`. The `CanvasEditing/` primitives (`ICanvasProjection`, `CanvasPointHandle`,
  `CornerPinHandles`, `CanvasItemPicker`) are a credible reusable foundation — this is the stack to grow.
  (`ICanvasPointSnapper` is a stub nothing implements; snapping bypasses it via `SurfaceGeometry.TrySnapOffset`
  — either route snapping through it in Phase 4 or delete the seam until then.)

### 2.3 Undo completion (~2–3 days)
~30 structural mutation sites (add/duplicate/delete surface/output/slice, all drag-drop rebinding) call
`SaveActive()` directly, against the spec's own "everything but selection is undoable". The entities are
cheap-to-clone DTOs — a generic `SetupStructureCommand` capturing before/after sub-state (or a whole-setup
snapshot) is correct and small. Include the `Setup.Id` guard (P0.2), and move `SaveActive` out of `Apply`
(held Ctrl-Z currently writes the file per step) — save once per user action.

### 2.4 OutputManager split (later, before Player support)
826 lines, five jobs (presentation, content resolution, compositing, calibration overlay, warp utility).
`Player/` references nothing in `T3.Core.Output` — the "player later" story depends on extracting the
compositor from editor concerns (aim crosshair, `ImGui.GetFrameCount()`-keyed messaging at `:311-328`).
Also: `_targets` never evicts render targets for deleted outputs; presentation handles only the first
bound output. Not urgent; don't let more editor state accrete into it.

---

## P3 — Per-frame allocation sweep (~1 day, mostly mechanical)

The author demonstrably knows the rules (`stackalloc`, reused static buffers, a cached collapse closure
with an explanatory comment) — the row-callback API design made compliance impossible. Fixes:

1. **`Setup.TryGetSurface/Output/Slice/Source(id)` for-loop lookup helpers** — replaces 58×
   `List.Find(x => x.Id == id)` capturing closures across four files in one move.
2. **`DrawEntityRow`: args struct + dispatch instead of 2–6 fresh lambdas per row per frame**
   (`SetupPanel.cs:91-96, 709-720, 1199-1209`), and a static-method-group (or cached) `ContextMenuForItem`
   callback (`:1722-1753`) per the explicit rule in AGENT_INSTRUCTIONS.
3. **Cache per-row strings** (labels, `$"Display {n}"`, `SliceLabel`, count suffixes, drag payload
   `$"{kind}:{id}"` built per row per frame at `:563` even with no drag) — invalidate on change.
4. `OutputWindow.State.cs:134`: `BackgroundColor = [x,y,z,w]` allocates a `float[4]` every frame; compare
   before assign. `SetupPanel.cs:703`: `FindAll(...).Count` allocates a list to count — use the existing
   counter loop.
5. Canvas: per-surface `new[]` viewQuad (`SetupOutputView.cs:407-413`) and per-frame arrays (`:248, :304,
   :344`) → reused buffers like the file already does elsewhere; `CanvasItemPicker.cs:63` `FindIndex`
   closure.

---

## P4 — Component hygiene (small, batchable)

1. **`Icons.DrawInlineGlyph(icon, color)`** — replaces the two private copies (`SetupPanel.DrawInlineIcon`,
   `FormInputsNarrow.DrawGlyph`, ~10 call sites). The baseline-aligned inline glyph is a real gap worth one
   shared helper.
2. **Promote `FormInputs.DrawCheckbox`** (private) with size/color params; delete
   `FormInputsNarrow.DrawCheckbox` — two checkbox looks will drift otherwise.
3. **`CustomComponents.MenuItemsDisabled`/`MenuItemsFlushLeft` ambient globals → parameters or a scoped
   ref-struct.** A leaked flag (early return between set/clear, e.g. `SetupPanel.cs:1725-1751`) restyles
   every menu in the editor. Also: the new field was inserted between `DrawMenuItem` and its xmldoc,
   orphaning the doc.
4. **`StateButton` grows `NeedsAttention`**; the Isolate toggle stops hand-rolling 4× `PushStyleColor`
   (`SetupOutputView.cs:564-573` — `StateButton` is used 10 lines below).
5. ~~`FormInputsNarrow`: keep~~ — **reversed 2026-07-29: delete it.** Properties move to the Parameter
   window at full width (`ui-restructuring-plan.md` Phase A), so the narrow-sidebar layout gap that
   justified it no longer exists; its only consumer (`SetupPanel`) dies with Phase B. Items #2 and #5
   of this list are therefore obsolete — the checkbox lives on in `FormInputs.DrawCheckbox`.
6. **Comment debris sweep:** stacked/orphaned `<summary>` blocks at `SetupOutputView.cs:1182-1208`,
   `SetupPanel.cs:824, 1030, 1561`, `OutputManager.cs:524-526`, `SurfaceGeometry.cs:145-153`. Member
   ordering in `SetupPanel` (public/private interleaved).
7. Minor: `ReferenceImageView.cs:232` raw `InputTextWithHint` → `FormInputs.AddFilePicker`; mixed
   `SmallButton`/`StateButton` styles in header rows; key setups by `Setup.Id` instead of name at the
   persistence boundary (rename would strand the file + `.t3ui` pointer); add round-trip tests for the
   newest fields (`ContentSource`, `Slice`, `Surface.ParentId/Kind/SliceId` — exactly the untested ones).

---

## Progress

- **2026-07-27:** P0 all fixed (quad drift, cross-venue undo guard via `SetupCommands.TryGetSetup`, slice
  selection validation, frame-loop `ActiveSetup` publication + project-close eviction, frame-driven
  debounced `ContentSourceSync`, once-per-frame sink invalidation). P1 decided (setup-side). `Setup.Find*`
  lookup helpers added; 73 closure call sites swept. `SetupActions` extracted (43 members; canvas no longer
  references the panel; +`RenameEntity`/`DeleteEntity`/`CanDeleteDirectly` kind dispatch). `EntityItem`
  extracted (delegate-free item widget: args struct, action-result, cached context-menu delegate, drag
  payload only built while active); `Icons.DrawInlineGlyph` replaces both private glyph copies; rename
  state moved into `EntityItem`. `SetupPanel` is now ~1,270 lines of pure panel layout/cards/highlights.
- **2026-07-27 (later):** UI consistency pass — one shared context menu (`EntityItem.DrawContextMenuItems`)
  for sidebar rows and canvas labels with uniform Duplicate/Rename/Delete (new `DuplicateEntity` via JSON
  clone; `DeleteEntity` covers ref-images/props with binding cleanup); drags direction-agnostic
  (`CanConnect` + normalization in `ApplyDrop`; outputs draggable). Selection completion, first slice:
  generic `SelectionSet<T>` (UiModel/Selection); sub-element plane in `SetupOutputView._canvasSelection`
  (corners as `SelectionTarget.Part=Corner`); selected-corner rendering; ctrl/shift on corners and on
  canvas entity picks; `SelectionFence` marquee over corners (Output mode, guarded); group corner drag
  across surfaces with MacroCommand undo; plane clears on canvas-view change. Slice-vs-surface snapping
  unified (sibling candidates, ~14° axis lock, guide lines in the slice editor).
- **2026-07-27 (drag unification):** the drag machines now share the snapshot→apply→commit skeleton:
  surface rectangle edits (edge crop, region edit, label move) all run through `RunResizeDrag` (label move
  converted from its hand-rolled state machine); slice edits (edge, corner, label move) through a new
  `RunSliceDrag` with a new `ChangeSliceRectCommand` — slice drags previously **saved the file every
  mouse-move frame and had no undo**, both fixed; "Match target aspect" is undoable too. Measure endpoint
  drags commit a new `ChangeAnnotationCommand`. Corner drags keep their group-aware skeleton (`HandleDrag`).
- **2026-07-28 (undo completion, P2.3):** every structural setup mutation is now one undo step via
  `SetupSnapshotCommand` (whole-setup JSON snapshots, restored in place so the live `Setup` reference
  survives) wrapped through `SetupActions.RunUndoable` — covers add/delete/duplicate for every kind
  (incl. multi-delete as one step), drag-drop connects, gutter bind toggles, renames, clear-content,
  sub-regions, "+ surface" mapping, measuring-line add/delete, and Straighten/Apply-lengths (both were
  save-only before). No-op edits (identical before/after JSON) push nothing. `DeleteSurfaceCommand`
  deleted — subsumed by snapshots. Known limits, documented in the command: display bindings live in the
  per-machine file, so undoing an output delete restores it unbound.
- **2026-07-28 (card field undo):** the property-card fields are undoable too — one-shot toggles
  (Render/Send/Show-raster/Lock-aspect) via `RunUndoable`; continuous drag-fields (position, anchor,
  raster subdivisions, slice px position/size) via `BeginFieldUndo`/`CommitFieldUndo` in SetupPanel, which
  snapshot on the gesture's first `InputEditStateFlags` event and commit one step + one save on Finished —
  slice px fields previously saved per mouse-move frame. Size (m) keeps its `ResizeSurfaceCommand` path.
  The Content card's Update toggle stays op-side (graph state, not setup data). Setup undo coverage is
  now complete except op-side edits, which have their own graph commands.
- **2026-07-28 (testing round):** user-test fixes — per-axis local snap thresholds (X-scale was applied to
  Y; slice editor too); straight framing is captured only at the settled state and *held* across all edits
  and releases (`_easeKeepsFraming`: a same-basis settle eases R inside the stationary window — the camera
  never chases an edit; only basis/mode changes re-frame); click threshold before a label grab becomes a
  move; whole-quad label move for top-level surfaces (press selects via picker with stack cycling, hold
  moves); multi-select shown on canvases; toolbar panel-toggle only when the panel is closed. New
  `SetupSanitizer` (UiModel/ProjectHandling): setups are hostile input — on load/switch it force-repairs
  non-finite/absurd/degenerate quads, invalid sizes/anchors, and **strips stray OutputMappings from Layout
  children** (a mapped region silently becomes an independent surface — the hierarchy-corruption bug), each
  with a warning, persisted immediately. Writers guarded: region→output drops and bind toggles refuse
  Layout children. Children are called **Region** in all display strings (card header, menus, default
  names); nested non-Layout surfaces are detached to roots on load. The harsh-snapping root cause was the
  **axis-lock cone** (capture zone = drag distance ÷ 4 → 100px+ on long drags — log-probed, point snapping
  was innocent): the lock now keeps its ~14° directional condition but caps capture at ~1.5× the snap
  threshold (~10px screen), constant at any zoom/distance; same fix in the slice move.
  **Open modifier-map decision (flagged in canvas-interaction.md):** Shift currently *suspends snapping*,
  but the plan wants Shift = *constrain to H/V/45°* — one key, two meanings; settle before muscle memory
  hardens (e.g. Shift = constrain, Alt = suspend).
- **2026-07-28 (instance-ification):** `SetupPanel` and `EntityItem` are per-window instances, owned by
  `OutputSetupModeView` and shared between the panel and `SetupOutputView` — rename state, hover
  cross-highlight, collapse sets, primary cache, and menu context no longer bleed between open output
  windows. Still static, deliberately: the Guid-list hooks (global registry), pure helpers, and the
  `_availableNames`/`_sinkContext` scratch buffers (single-threaded reuse, no per-window semantics).
  Next per the pre-flow-view list: remove the `_focusedSurfaceId`/`_selectedSliceId` selection shadows,
  then round-trip tests for the newest serialized fields, then the P4 hygiene batch.
  **Still open from P2.2:** the `_focusedSurfaceId`/`_selectedSliceId` selection shadows; the bypassed
  `ICanvasPointSnapper` seam; sub-element selection for slice corners / annotation endpoints.
- **2026-09-04 (shared selection + pinning):** `SetupEntitySelection` is one shared instance
  (`OutputSetupHandling.EntitySelection`); windows follow it and carry only a per-window **entity pin**
  (breadcrumb menu + toolbar indicator, persisted via `OutputWindowState.PinnedEntityKind/Id`, stale pins
  self-revert). Also fixed in passing: the breadcrumb's `drawExtraMenuItems` callback was never invoked.
  User-tested. Slice doc: `shared-selection-slice.md`.
- **2026-09-04 (Phase A — properties → Parameter window):** new `SetupParameterView` draws the selected
  entity's card in the Parameter window; arbitration by recency via `GlobalSelectionHandling` version counters
  (`NodeSelection.LastChangeStamp` vs `SetupEntitySelection.LastChangeStamp` — most recent pick wins; a
  selected SendToOutput whose ContentSource is the primary keeps op parameters and gets the setup side
  appended inside the parameters area: resolution + slice/target summary). Cards rewritten on `FormInputs`
  conventions (label column, capped field width, hairline-gap vector rows); editable Name field commits
  one undoable rename on blur; ref-image and prop kinds gained cards (prop height editable). Removed:
  `SetupPanel`'s properties footer + card methods + field-undo helpers (moved), `DrawEntityCard`
  (output-area fallback now an empty-state pointer), **`FormInputsNarrow` deleted** — P4 items 2/5
  obsolete as planned. Guid-list hooks moved to `SetupParameterView`. Verified via the debug bridge
  (sink extras render below op params; shared selection mirrors across two windows).
- **2026-09-04 (REWORK AGREED, not yet done) — replace the recency arbitration:** the version-counter
  mechanism (`GlobalSelectionHandling.NextVersion` + `LastChangeVersion` on both selections) is a
  bolt-on judged sub-par: it lets the graph node *stay visibly selected* while the Parameter window
  shows an entity — violating the "never two selected things" principle — and `SetSelection` draws
  three version numbers per click (Clear + AddSelection + SetSelection all stamp). **Agreed
  replacement: an explicit inspection target** — one slot on `GlobalSelectionHandling` saying what
  the Parameter window shows, set directly at pick time by both systems, cleared by whoever owns it;
  gives the graph a hook to visually deselect when an entity takes over. Rework scope: delete the
  stamping from `NodeSelection` + `SetupEntitySelection`, add the target slot, rewire
  `SetupParameterView.TryDraw`, fold the near-duplicate `DrawFloatsRow`/`DrawIntsRow`, stop
  shadowing FormInputs' width const (`MaxFieldWidth = 280`). Everything else from Phase A
  (cards, port, removals, sink extras) stands and is user-tested.
- **2026-09-04 (rework done — explicit inspection target):** the version counters are gone.
  `GlobalSelectionHandling` now holds one slot, `InspectionTarget` (None / GraphNode / SetupEntity), that
  says what the Parameter window shows. A pick **claims** it (`NodeSelection.AddSelection` /
  `SetSelectionToComposition`; `SetupEntitySelection.Select/Add/Toggle`) and claiming **clears the other
  system's selection** — so the graph node visibly deselects when an entity takes over, and vice versa:
  never two selected things. Owners **release** on `Clear` (and `SetupParameterView` releases when the
  primary no longer resolves). The graph→CONTENT-row mirror in `OutputSetupModeView` uses a new
  `SetupEntitySelection.Mirror` (sets without claiming) and runs only while the graph owns the inspection
  (`_graphOwnedInspection` tracks the ownership flip, so a background click right after an entity pick
  still closes the panel). `SetupParameterView.TryDraw()` takes no argument anymore and the ContentSource
  mirror special-case is gone (an entity claim empties the graph selection, so it can't occur). Also:
  `DrawFloatsRow`/`DrawIntsRow` share `BeginValuesRow`/`EndValuesRow`; `FormInputs.MaxNumberInputWidth`
  is `internal` and used directly. Manual test `output-setup-parameter-window.md` step "Picking takes the
  window" rewritten for the new behavior. Not yet user-tested.
- **2026-09-04 (vocabulary):** "sink" stays only on the Core API (`IOutputSink`, `OutputSinkRegistry`);
  Editor members and comments say **send op** (`DrawSendExtras`, `FindSendInstance`, `AddContentSend`,
  `TryGetSendOutput`, …), and the output window's panel is the **setup panel** everywhere (`DrawSetupPanel`,
  no more "sidebar"/"side panel").
- **2026-09-05 (folder split):** the projection-mapping UI moved to `Editor/Gui/Windows/OutputSetup/`
  (namespace `T3.Editor.Gui.Windows.OutputSetup`): `Setup*`, `EntityItem`, `OutputSetupModeView`,
  `OutputManager`, `ContentSourceSync`, `SurfaceGeometry`, `ReferenceImageView`. `Windows/Output/` keeps
  only the output window itself (window, toolbar, state, camera, resolution). The Phase B outliner and
  Phase C board land in the new folder.
- **2026-09-05 (anchor / Y-up pass):** `Surface.Anchor` (signed −1..1, Y-up, default bottom-centre)
  replaces `StagePlacement.Pivot`; surface-local space is now Y-up with the origin at the anchor.
  `SurfaceGeometry` rewritten around `LocalRect`/`LocalBounds`/`ApplyBounds`/`ChildBounds`: a crop re-derives
  the anchor so the origin never moves, which deleted the annotation counter-move and its undo snapshot.
  Region creation, snapping, edge drags, the raster origin and the Straight framing (`AnchoredRect`) all
  converted; inventory + rationale in `anchor-yup-pass.md`. No legacy conversion (internal preview).
  Not yet user-tested.
- **2026-09-05 (Patches, slice 1):** `OutputDefinition.Patches[]` replaces the single direct `SliceId`
  (data-model §2.5). Model + JSON + round-trip test; `Setup.FindPatch`; renderer draws patches under
  surfaces; `EntityKind.Patch` with rows under their output (collapsible), input-gutter bind toggle,
  drop routing (output → new full-canvas patch, patch → re-feed), "Add Patch" on the output menu,
  Duplicate/Rename/Delete, sanitizer quad repair, Parameter-window card (px position/size). Icon is a
  `Icon.Patch` (atlas slot 161, added by the user). Manual test `output-setup-patches.md`. User-tested.
- **2026-09-05 (Patches, slice 2):** canvas editing in `SetupOutputView.DrawPatches` (corner/edge/label
  gestures on one snapshot skeleton `RunPatchQuadDrag` + `ChangePatchQuadCommand`; snapping to canvas and
  sibling patches), `SetupActions.SplitOutput` and `PromotePatchToSurface`, menu entries on output and
  patch. Manual test extended. User-tested.
- **2026-09-05 (P2.2 leftovers):** the selection shadows reviewed. `_selectedSliceId` (written from the
  pick handler *and* the draw parameter) is now a local of `DrawSourceCanvas`; `_focusedSurfaceId` is
  `_shownSurfaceId` — not a copy of the selection but the window's shown surface (selection or pin),
  set at the top of every `Draw` and documented as frame-scoped; `SetupPanel._primaryKind/Id` stays as a
  once-per-frame resolve (its comment says why). `ICanvasPointSnapper` deleted with its
  `CanvasPointHandle.Draw` parameter: nothing implemented it and every snap runs after the handle, in the
  edit's own space (parent metres, output px) rather than canvas space, so the seam could not have fit.
  Still open from P2.2: patch corners (and slice corners / annotation endpoints) in the sub-element plane.
- **2026-09-05 (Phase B.1 — `SetupRelations`):** the routing graph is one queryable unit
  (`Editor/Gui/Windows/OutputSetup/SetupRelations.cs`): `CollectRelated` (both directions, into a caller
  buffer, `Relation{Kind, Id, IsConsumer}`), `IsDirectSourceOf`, the walks `TryGetSurfaceOutput` /
  `TryGetSendOutput` / `TryGetSliceSource` / `TryGetPatchOutput` (out of `OutputSetupModeView`), and the
  predicates/counts `IsSliceOf`, `IsMappedTo`, `OutputShowsSlice/Source`, `CountSlicesOfSource`,
  `CountConsumersOfSource`, `CountChildren` (out of `SetupActions`/`SetupParameterView`). The panel keeps
  only its hover buffer and gutter lookups. No structure-version cache yet — the queries are loop-based
  into reused lists; B.6 caching lands with the outliner that needs it.
- **2026-09-05 (Phase B.2 + B.3 — the Flow Outliner strip):** `SetupPanel` → `SetupFlowOutliner`: a strip
  under the canvas (`OutputSetupModeView.DrawOutliner`, `OutlinerReservedHeight`; `OutputWindow` sizes the
  canvas child to what's left) with a full-width up/down splitter, a header (setup switcher · breadcrumb
  from `SetupRelations` · collapse chevron) and columns CONTENT / SURFACES / OUTPUTS / LOCAL BINDINGS plus a
  shelf (REFERENCE IMAGES, PROPS) inside one scrolling child. Rows are the same `EntityItem`s, which learned
  a column rect (`Args.ColumnMinX/Width`); inventory rows (`EntityKind.None`) are inert. LOCAL BINDINGS
  lists this machine's displays from `Screen.AllScreens`, dimmed while free, showing the bound output's
  name otherwise. Toolbar: "Show Flow Outliner" menu item + `Icon.ViewList` toggle (placeholder glyph for a
  bottom panel). Not done: edges (B.4), merged output/binding pills, collapsed-count badges, per-setup
  collapse persistence. Manual test `output-setup-flow-outliner.md`; older sets reworded. User-tested.
- **2026-09-05 (Phase B.4 — edges):** `SetupFlowOutliner.DrawConnections` draws the routing as bezier links
  between item anchors (`EntityItem.LastRowRect`, collected per frame into `_anchors`; a folded item attaches
  to its drawn parent): slice→surface, slice→patch, surface→output per mapping, output→plug; unbound outputs
  get a `StatusAttention` stub. Colour is `StatusAutomated` (the "linked" status hue), faded at rest, full +
  thicker while an endpoint is hovered or selected. Drawn in a lower draw-list channel so the items stay on
  top and clickable. Deferred: clamp/fade stubs for scrolled-out items, mapping-stack badges at the output
  end, edge hit-testing (needs `EntityKind.Mapping`). Not yet user-tested.
- **2026-09-05 (gutters retired, B.7 part):** with connections on screen the item decorations that mirrored
  the routing went: trailing target icons + "×N" counts, routing name statuses, hover-lit input arrows and
  the `_referenced` hover trace. `EntityItem.Args` lost `TrailingIcon`/`HighlightInputArrow`/`HighlightTrailing`;
  `Status` is now only non-routing text (a plug's resolution). The click-to-bind arrow stays and its gutter is
  reserved only while a bindable source is the primary. Not yet user-tested.
- **2026-09-05 (B.5 + B.6):** Del on the focused strip deletes the selection (guarded by `IsAnyItemActive`,
  so a rename field keeps the key). `OutputSetupHandling.StructureVersion` bumps in `SaveActive` — the one
  funnel for every mutation; `SetupFlowOutliner.RefreshCaches` rebuilds the connection list, the unbound
  outputs and the derived slice/patch labels only on a tick or a setup switch, and the breadcrumb follows
  the same tick instead of a frame timer. Anchors still come from the draw pass (free, no allocation).
  Not yet user-tested.
- **2026-09-05 (Phase C.1 + C.3 — Board v1):** `CanvasPlacement { Position (m), PixelsPerMeter }` on
  `ContentSource`, `OutputDefinition`, `ReferenceImage` and root `Surface` (additive JSON, round-trip test).
  `SetupOutputView.Board.cs` draws the Board on its own `ScalableCanvas` through a Y-up `BoardProjection`:
  `MetricGridRaster` (log-blend decades, "n m" labels, floor line), cards per kind (surfaces at true size
  standing on the floor with regions nested; content/output/reference as pixel cards at a presentation
  scale with live thumbnails, slices and patches as sub-rects; props as figures), name chip + muted meta
  (per-structure cache), whole-card pick/drag with one undo step per gesture (`SetupActions.CommitGesture`),
  a top-right handle scaling pixel cards' px-per-metre only, double-click → the entity's space. Seeded
  kind-grouped layout persisted once. Entered via `EditMode.Board` (first tab) or as the default when the
  outliner is shown and nothing focuses a space (`DrawBoardStandalone`). Not done: C.2 fading/morph,
  C.4 consolidation, grid snapping, resolution badges, photo backdrops on surface cards, ghost frames.
  Manual test `output-setup-board.md`. User-tested.
- **2026-09-05 (Board v1 test round):** selecting never leaves the Board — `SetupOutputView.ShowsBoard` gates
  the per-kind routing in `OutputSetupModeView` (content/slice open their source canvas, a reference image
  its view, only by double-click; `OpenedReferenceImageId`), and the source canvas / reference view got a
  **Board** button back. Zoom was clamped by `ScalableCanvas`' px-per-px range: a `BoardCanvas` subclass
  clamps in px-per-metre instead. `SelectionFence` marquee over the cards (overlap; shift adds, ctrl removes,
  empty click clears), group drag of every selected card (a plain press on a selected card keeps the set and
  single-selects on release), **F** frames the selection or everything (`UserActions.FocusSelection`).
  `MetricGridRaster` adopted the timeline rasters' 1 → 5 → 10 log-blend ladder (same `Density`), so the Board
  densifies like the curve editor; the drawing stays its own (two axes, Y-up, floor line) — the timeline
  classes are canvas-scroll, BPM and Y-down coupled, so only the ~10 lines of spacing math transfer.
  Outliner: kind colours via `SetupColors` (content/slices = texture type colour, surfaces =
  `StatusControlled`, outputs neutral) on items, column headers and connections; the click-to-bind arrows
  no longer appear for a *source* primary (which slice would bind was ambiguous and the lit arrows on every
  consumer read wrong); the unbound-output stub became a muted "unbound" status. Reference-image card in
  the Parameter window gained the path field (file picker). Open: whether `CanvasPlacement` should leave
  Core for an editor-side sidecar (view state, like collapse sets) — user question, undecided.
- **2026-09-05 (Board surface edges):** the selected surface card carries the Straight view's edge handles
  (`DrawBoardSurfaceEdges` → `RunResizeDrag` + `SurfaceGeometry.DragEdge` with the card's placement as the
  origin): an edge crops, Ctrl stretches, the corner pin follows, one `ResizeSurfaceCommand` per drag. The
  fence guard now reads the Board's own gesture state instead of `IsAnyItemActive` (a background press
  makes the window's move-id active, which vetoed every fence).
- **2026-09-05 (reference images via assets):** the Image field (Parameter window card and reference
  view) is the LoadImage-style type-ahead asset picker filtered to the image asset type
  (`SetupActions.ImageFileFilter`); `ReferenceImage.FilePath` holds an asset address. The Board is a drop
  zone (`HandleBoardDrop`): an Asset Library image or an OS file becomes a reference image card at the drop
  point (`SetupActions.AddReferenceImageFromFile`, importing OS files into `Assets/images/reference` via
  `FileImport` unless they already are assets). Styling of Board cards and outliner items deliberately
  deferred (user, 2026-09-05) in favour of riskier work.
- **2026-09-05 (Phase C.2 — spaces fold out of the Board, v1):** the second `ScalableCanvas` is gone;
  every space (an output's canvas with its Straight/Content morph, calibration, a source's texture) draws
  through `SpaceProjection` — space px, origin at the entity's Board card top-left, scaled by the card's
  px/m — on the Board's canvas. `EnterSpace` drives `_spaceBlend` (0 Board … 1 space, same easing as the
  morph): the camera eases onto the space's fit (`FitToArea`/`FitScopeWithMargin` now fit the Board camera
  to the view-px area in metres) and back to the remembered Board view; `DrawBoardLayer` draws the cards
  the space doesn't own faded by the blend (inert below full opacity) and skips the ones it does
  (`IsDrawnBySpace`: the space's entity, and for an output the surfaces mapped to it); mapped surfaces'
  quads lerp from their Board card rect (`TryGetBoardQuadInView`) to R(quad). Handles editable only at
  blend 1; one `ResolvePicking` per frame at the end of each entry point. Known v1 seams: regions appear
  only once settled, the reference view is still its own canvas (C.4), the no-basis Content preview draws
  content px over the output card, `DampScaling` stops easing above scale 1000 (only our own lerps ease
  there). Not yet user-tested.
- **2026-09-05 (tracing on the Board — C.4 part):** `ReferenceImageView` deleted. `SetupOutputView.Reference.cs`
  is the image's space (`DrawReferenceCanvas`, entered by double-click, `OpenedReferenceImageId` kept while the
  selection stays on the image or a surface traced on it — `OutputSetupModeView.IsInReferenceSpace`): the
  photo at px through `SpaceProjection`, traced quads as `CornerPinHandles` in image px (one setup-snapshot
  undo step per drag), Photo/Straight morph ported (`TryRenderStraightened`). The Board draws the traced
  quads on the image card read-only (`DrawBoardTraces`, surface colour, labels pick the surface). Tracing
  starts from the image card's menu: `SetupActions.TraceNewSurface` / `TraceSurfaceOnImage`. Texture cache
  moved to `ReferenceTextureEntry` (mips generated once per load). Also: the header's "+ <surface>" mapping
  buttons draw only with an output and off the Board (they crashed on the standalone Board). Not yet
  user-tested.
- **2026-09-05 (fold camera, measured):** the Board ↔ space transition eases once, not twice: the camera
  interpolates from the view the user had to the *settled* rect (`GetSettledBoardRect`: output card /
  surface card + surround) with a geometric zoom and a linear centre (`BlendScopes`), and Straight anchors
  the rectified rect's **centre** on a straight screen-space line from the output card to the surface card,
  solving the projection origin through that frame's camera. Measured over the new bridge method
  `outputSetup` (select entity + tab, no mouse) with the `[fold] metrics` probe: path-over-chord went
  3.2 → 1.00, chord deviation 51 px → 0, the centre lands on the window centre. Also: Board-side slice
  editing (the source editor pointed at the content card), the surface's slice previewed on its traced quad
  (`AddImageQuad`, faded), background pick priority for cards (`CanvasItemPicker.AddTarget(isBackground)`)
  so labels on cards win, and a label pick cancels the card's group-drag grab. Open: UI screenshots via
  `screenshot target:"ui"` come back black from a `start`-launched Release editor (window not composited?).
- **2026-09-05 (Straight = in place on the photo):** user clarified the intent: Straight for a surface traced
  on a reference image rectifies *that photo in place* (the photo's corners push outward, the traced corners
  ease into an upright rect with the surface's aspect, labels fade, the camera settles on the rect + surround);
  the surface's own Board card is not involved. The slide-onto-the-surface-card and the photo ghost in the
  output space were removed; the output-space Straight stays inside the output card. Routing: `Draw()` /
  `DrawBoardStandalone` enter the ReferenceImage space for a traced shown surface on the Straight tab
  (`TracedImageOf`, `DrawReferenceSpaceForShown`); `EnterSpace` keeps a fading space's identity; the Straight
  tab is enabled for traced surfaces even without an output. Photo ↔ Straight is eased on the morph timing
  (`_referenceProgress`) and a subject change while straightened eases quad and target from the previous
  surface's (`ResolveStraightSubject`, `_referenceSubjectProgress`). The projector-camera header is
  Calibrate-only (it pushed the canvas down at fold start). Measured: still to verify over the bridge with
  the user's editor (user runs it with `--debug-server 9042`; no auto-launch loops).
- **2026-09-05 (photo flow completed, user-tested fold):** the in-place straighten is confirmed straight
  (`[fold] metrics` 1.00 / 0 px) and switching subjects eases (`ResolveStraightSubject`). Added: corner and
  edge handles on the rectified rect (`DrawStraightEdits` — the rectification is frozen for the drag and eases
  to the refined trace on release; edges crop axis-aligned), the measuring-line tool on the photo
  (`DrawAnnotations` now takes a surface↔view homography pair; `TryStraightenTraceFromLines` refines the
  trace, Apply lengths unchanged), the surface card's backdrop is the straightened photo crop
  (`TryGetTracedFragment`, per-surface warp targets via `RenderWarpedTexture(targetKey)` at ≤1024 px), a
  Board edge crop also crops the trace (through the old rect's projection; the gesture is one setup
  snapshot), and card metadata refreshes during a drag. Probes trimmed to the metrics line.
- **2026-09-05 (live trace refinement):** user rejected the freeze-then-ease on release. Now the rectified
  rect is sticky (`_referenceSticky*`, re-derived only for a new subject or a changed size) and a handle drag
  moves the trace live through the press-time rect→photo mapping, so the photo re-warps under a fixed frame
  and release does nothing.
- **2026-09-05 (surface scaling on the Board):** a round top-right handle scales a surface uniformly about its
  bottom-left; Ctrl on an edge handle (circles) scales along that axis, aspect free; plain edges crop as
  before. `ApplyBoardScale` re-projects the pins through `ApplyBounds` and scales lines and regions about the
  fixed corner, applied incrementally so a live drag never compounds. Scaling leaves a trace alone (same wall);
  cropping still crops it. `CornerPinHandles.Style.EdgeHandleShape` carries the circle/square look. Measuring
  lines on the photo no longer thicken while dragged (`DrawAnnotations(projected: false)`).
- **2026-09-05 (content preview opacity):** `UserSettings.Config.OutputSetupContentPreview` (default 0.65), a
  "content NN%" slider in the canvas header; used by the traced quads on the image card and by the surface
  card (content over its photo fragment).

## Suggested order (revised for the flow-view pivot)

1. **P0** (bug fixes, 1–2 days) — independent of every decision below.
2. **P1** (routing-model decision) + the mapping join-table sub-choice — one sitting with the doc open;
   now gates the flow view, not just planning.
3. **Extract `SetupActions` + `SetupRelations` + the `EntityItem` widget from SetupPanel** (~2 days) —
   the flow view's action layer, data source, and node body respectively. Include the `Setup.TryGet*`
   lookup helpers (P3.1) and the no-per-item-delegates redesign here — they serve every consumer.
4. **P2.2** (selection completion, ~1 week) — before building the flow view, so it and the canvas share
   one selection stack instead of cloning the ad-hoc parts.
5. **Canvas cleanup** — the SetupOutputView items: drag-state-machine unification (P2.2), per-frame
   allocation fixes in the canvas paths (P3.5), comment debris. The canvas is staying; this is where
   cleanup compounds.
6. **P2.3** (undo completion) — any time after P1; pairs well with `SetupActions`.
7. **P4** — batch in where files are already open; skip the row-specific items (superseded by the rewrite).

Not worth doing: unifying with `NodeSelection`; demoting `Homography` from Core (leave it; note
`ProjectorSolver`/`LineRectifier` as demotion candidates only if Core-minimalism pressure grows);
any schema migration of the serialized formats — they're additive-extensible as-is.
