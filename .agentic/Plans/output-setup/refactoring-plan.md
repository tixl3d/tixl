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
5. `FormInputsNarrow`: keep (the narrow-sidebar layout gap is real, `Span`-based value edit composition is
   right), but fix #2/#4 above and replace `DrawCardHeader`/`DrawSmallText` bodies with `StylizedText`.
   Note the comment-only `RightMargin = 6` ↔ `Indent(6*scale)` coupling with SetupPanel.
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
  **Still open from P2.2:** the `_focusedSurfaceId`/`_selectedSliceId` selection shadows; routing snapping
  through `ICanvasPointSnapper` (behavior matches, seam still bypassed); sub-element selection for slice
  corners / annotation endpoints; annotation add/delete undo (goes with P2.3 structural undo).
  Then undo completion (P2.3).

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
