# Output Setup — UI Restructuring Plan (2026-07-28)

Successor plan to the UX pivot decided after user testing (code review in
[`refactoring-plan.md`](refactoring-plan.md); the interim `flow-view.md` design notes were absorbed
into this plan and removed 2026-07-29). Four moves, dependency-ordered:

1. **Properties move to the Parameter window** (new fallback mode, like section settings).
2. **The side-panel tree is replaced by a horizontal Flow Outliner** at the bottom of the output window.
3. **`FormInputsNarrow` is deleted** — with the narrow side panel gone, the standard `FormInputs`
   widths apply everywhere.
4. **One unified canvas** shows contents, surfaces and outputs together; per-space views (output,
   straight, content) distort only the entities that participate in that space — everything else fades
   instead of warping; an adaptive metric grid sits in the background.

What this supersedes: the sidebar layout parts of `output-settings-spec.md` (tree sections, gutter
icons, property cards in the panel). What survives unchanged: the data model (`data-model.md`, all
settled), the row-state matrix (`states.md` — states transfer to outliner items), selection
(`selection.md`), canvas interaction (`canvas-interaction.md`), undo policy, and everything already
extracted for this pivot (`SetupActions`, `EntityItem`, `SelectionSet`, `SetupSnapshotCommand`).

## Glossary (canonical terms — 2026-07-29)

Specs, code, and discussions use these; aliases listed are **deprecated**.

**Views**
- **Board** — the 2D unfolded overview canvas; the home view. *(replaces: OutputCanvas, "unified canvas", "overview")*
- **Stage** — the 3D venue view.
- **Camera presets** — Output (through a projector's frustum), Straight (ortho facing a surface),
  Content (ortho onto a source). Cameras entered via entities, not modes (open question 5).
- **Flow Outliner** — the bottom panel: columns + pills + connections. *(avoid: "strip", "sidebar")*

**Entities** (generic term: **Entity** — matches `SetupEntitySelection.EntityKind`; an *Item* is an
entity's outliner representation, never the model object — see Representations)
- **Content** (source), **Slice**, **Surface**, **Region** (child surface — display name, settled),
  **Output**, **ReferenceImage**, **Prop**, **Machine**; future: Camera, Fixture, StageModel,
  ReferencePoint.
- **Output** — a logical pixel canvas; "the part that must survive the cable being pulled".
- **Endpoint** — anything an output can be bound to on a machine: a Display (physical or virtual),
  a Spout/NDI sender, later an Art-Net node. *(replaces: "Device" — too hardware-flavored for
  Spout/NDI; "Endpoint" matches the WASAPI vocabulary the audio abstraction already uses)*
- **Virtual display** — an always-existing display endpoint (`2nd Display`, `Editor Display`).
- **Patch** (2026-08-31) — an output-side canvas region on the direct pipe:
  `{ SliceId, Quad, Name }`; an axis-aligned rect = packing tile, a warped quad = surface-less
  keystone (data-model §2.5). *(leaning "Patch"; "Tile" recorded as the alternative — never "slice",
  one name per end of the pipe)*

**Relations** (all drawn as **Connections** — the view-level word for any line)
- **Route** — slice → surface/output content assignment.
- **Mapping** — the surface → output edge; carries quad + modifiers.
- **Binding** — the output → endpoint assignment, per machine. **Reserved for exactly this** — a
  Binding is *one kind* of connection, never the generic word for a line (collision resolved in
  favor of `DeviceBinding`/`binding-examples.md` usage).
- **Derivation wire** (2026-08-31) — a **dashed** connection for authoring dependencies (reference
  jig → derived slices, data-model §2.10); solid wires are always the render path.

**Representations & modifiers**
- **Card** — an entity's canvas representation on Board/Stage (ContentCard, SurfaceCard,
  OutputCard, ReferenceCard, PropCard).
- **Item** — an entity's Flow-Outliner representation (drawn by `EntityItem`; nests naturally once
  hierarchy becomes containment). *(replaces: "pill", "row" — both bake in a flat shape; confirmed
  2026-09-05: manual tests and UI text say **item**, e.g. "the Surface 1 item in the SURFACES column")*
- **Modifier** — a verb-attached refinement on a mapping or output: Mask, Warp, ColorCorrection.
  *(replaces: "SubEntity", "Filter" — SubEntity collides with the selection model's* sub-element*)*
- **Sub-element** — a canvas handle (corner, lattice point, annotation endpoint); the second
  selection plane (`selection.md`). Not an entity.

---

## Phase A — Properties → Parameter window

### A.1 One shared selection + per-window pinning (revised 2026-08-29)

Supersedes the per-window model with an `ActiveEntitySelection` last-focused-wins indirection:
selection is now **one shared `SetupEntitySelection` across all output windows** — full decision and
consequences in [`selection.md`](selection.md) §Selection scope. The Parameter window reads it
directly; there is no "which window feeds me" rule to implement.

- What per-window selection actually provided — *each window deciding what it displays* — becomes an
  explicit per-window **pin**: a window follows the shared selection by default; pinned (via its
  breadcrumb) it stays on its target while selection roams. **Pinning lands in the same slice as the
  sharing** — without it, a second output window is useless.
- Conflict resolution against the graph selection is an explicit **inspection target**
  (`GlobalSelectionHandling.InspectionTarget`, 2026-09-04): whichever system the user picked in last
  owns the Parameter window, and claiming it clears the other system's selection — never two selected
  things. `OutputSetupModeView.TryDrawEditingView` keeps only the sink→CONTENT-row mirror (a
  non-claiming `Mirror`), guarded on the graph owning the inspection.

### A.2 Parameter window hook

Extend the fallback chain in `ParameterWindow.DrawContent` (same pattern as
`DrawSettingsForSelectedSections`):

1. **inspection target is the setup entity → `SetupParameterView.TryDraw()`** (checked first: an
   entity claim empties the graph selection, which would otherwise fall back to the composition),
2. instance/input selected → op parameters (unchanged),
3. sections, 4. snapshots (unchanged).

Special case — primary is a `ContentSource`: that selection mirrors a focused `SendToOutput` op, and
the op's own parameters (Texture, Update, Color) are the more useful surface. Draw the **op's
parameters first, then the content card** (label/short-name, routing summary) below it. This is the
one case where op params and setup properties are on screen together, and it's intentional: they
describe the same thing.

Multi-select: draw the primary's card plus a muted "`+N` more selected" line. Batch editing is out of
scope (note as future).

### A.3 `SetupParameterView` (new, `Editor/Gui/Windows/OutputSetup/`)

Port the card bodies out of `SetupPanel` (`DrawSurfaceCard`, `DrawOutputCard`, `DrawContentCard`,
`DrawSliceCard`, `DrawEntityCard` for ref-images/props, `DrawMeasuredSizePopup`, `ApplyMeasuredSize`,
`PickTarget`/`ResolveTargetLabel`, `ConstrainSize`):

- **Rewrite the field rows on standard `FormInputs`** (full-width labels, `AddFloat`/`AddInt`/
  `AddCheckBox`/section headers). No narrow variants. Add unit suffixes (`m`, `px`) via the existing
  formatting hooks — this absorbs the "narrow form inputs" wishlist entry in `long-term-features.md`.
- **Move `BeginFieldUndo`/`CommitFieldUndo` along** (gesture-scoped `SetupSnapshotCommand`); the undo
  behavior is already correct, only the home changes. One-shot toggles keep `RunUndoable`.
- Header: entity icon + kind label + inline rename (reuse `EntityItem`'s rename state via the active
  window's instance? No — keep rename in the outliner/canvas; the card header shows a plain editable
  name field bound to `SetupActions.RenameEntity`).
- The card set is per `EntityKind`; the upcoming mapping stack (warp/mask/corner colors, data-model
  §2.3) lands here later as collapsible groups — the Parameter window has the vertical room the side
  panel never had. Leave a `// mapping sections land here` seam only in the plan, not in code.
- **Property fields are authored in code — no InputUi settings layer (2026-08-29):** the per-input
  configuration (e.g. marking an input as a Rotation field) exists because op inputs are generic;
  setup-entity cards are hand-written, so a future surface Rotation property simply *always* renders
  as a rotation field with 90° step buttons. (Rotation itself stays deferred —
  `canvas-interaction.md` §4.)
- **Surface size presets (2026-08-29):** the surface card gets a presets dropdown (27"/54" display
  16:9, common wall sizes) that writes Size — teaches that surfaces are screens as much as
  projection walls.
- **Modifiers are verbs, not entities (2026-07-29):** masks/warp/color are never added via a column
  `+` — they attach to the *selected* thing, via a canvas context-menu verb ("Add mask…" → draw mode
  in the matching camera view) and a "+ modifier" row on the entity's card. The selection context
  routes the placement (output selected → whole-canvas, output-level; surface-in-output → that
  mapping); the card subtitle states the resolved placement. Adding a modifier never materializes
  entities the user didn't ask for (output-level mask covers the direct-pipe case). Blend ramps
  don't use this door — they come from the "+ projector" helper. Content-shaping stays graph-side:
  the setup masks where light lands, ops shape what the image is.

### A.4 Removals in this phase

- `SetupPanel.DrawPropertiesFooter` + all card methods + field-undo helpers (moved).
- **Delete `FormInputsNarrow.cs`** (only consumer is SetupPanel — verified by grep). The
  refactoring-plan P4 items 2 and 5 (checkbox dedup, card polish) become obsolete.
- `OutputSetupModeView.TryDrawEditingView`'s `_panel.DrawEntityCard(...)` fallback (line ~107): kinds
  without a canvas (Prop, unplaced ref-image) no longer draw a card in the output area — show the
  last canvas (or empty-state message) and let the Parameter window carry the properties. Once Phase C
  lands, every kind has a home on the unified canvas and this case disappears.

**Shippable state after A:** side panel still exists but is tree-only (structure + selection); all
property editing happens in the Parameter window.

---

## Phase B — Flow Outliner (bottom strip)

### B.1 Prerequisite extraction — ✅ done 2026-09-05 (`SetupRelations`, see refactoring-plan progress)

- **`SetupRelations` (new):** move `ComputeReferenced`, `AddOutputsOfSurface`, `AddSourceOfSlice`,
  `AddConsumersOfSource/Slice`, the `IsHover*Highlighted`/`IsSourceOfPrimary` logic and the parent-walk
  helpers out of `SetupPanel` into one queryable unit: for any entity, its upstream/downstream
  neighbors. The outliner's edges are literally a rendering of this graph; the canvas fading rules in
  Phase C consume it too. Build it allocation-free: reusable lists, resolved per structure change (see
  B.6), not per frame.

### B.2 Layout & hosting — ✅ done 2026-09-05 (view-mode control stays on the canvas until Phase C)

- **Window layout (confirmed 2026-09-04): the output window stacks vertically — Board on top, Flow
  Outliner below**, separated by one splitter that runs horizontally across the full window width and
  drags up/down. Nothing docks to the side anymore.
- New `SetupFlowOutliner` (per window, owned by `OutputSetupModeView`), drawn as a full-width child at
  the **bottom** of the output window; the Board gets the remaining height above.
- Height: the splitter is the outliner's top edge (port `DrawPanelSplitter`, turned 90°); session state
  like the old panel width. Collapsible to a slim header bar; the existing `_showSetupPanel` auto-open logic
  (focused sink opens it, other op selection closes it) transfers as-is.
- Header row (left→right): **setup switcher** (port `DrawSetupSwitcher`), the **view-mode segmented
  control** (moves here from the canvas header, per the sketch), the breadcrumb (`Slice 2 → S2 → P1`),
  right-aligned: settings + collapse icons. Hovering the breadcrumb highlights that path's items and
  edges in the strip.

### B.3 Columns & rows — ✅ first version 2026-09-05 (no merged pills, no collapse-count badges yet)

Fixed columns with persistent muted headers + per-column `+` menus (reuse the existing add menus):

| CONTENT | SURFACES | OUTPUTS | LOCAL BINDINGS |
|---|---|---|---|
| sources, slices indented under them | roots, regions indented | outputs (auto-created; merged into their binding pill while 1:1 — see below) | **machine-grouped** transports from `MachineConfig`: displays, Spout/NDI streams (data-model §2.5) |

- Rows are **`EntityItem`** bodies — extend it to draw at a given rect/column width instead of the
  tree's indent metrics. Rename, context menus, drag-source, and the full `states.md` state matrix
  carry over unchanged. Nesting stays **indentation** (density beats containment in a text view —
  settled: density beats containment in a text view; true-proportion nesting belongs to the board
  and stage, not the outliner).
- Collapse: disclosure at the row's **right** end (left edge belongs to incoming edges); collapsed
  parents show a count badge; port `ToggleSourceExpanded`/`ToggleSurfaceExpanded`. Persist collapse
  sets with the setup later (`long-term-features.md` already wants this).
- LOCAL BINDINGS renders transports from the machine file (name-first, index-fallback), **grouped by
  machine** and labeled `Machine / Target` (e.g. `Local / Display 2`, `Local / Spout "Spout1"`) —
  single-machine setups show one implicit **Local** group. The column is an **inventory of available
  plugs** (unused ones listed dimmed), not a routing summary — edges show which are occupied. This
  resolves the Output-vs-binding ambiguity and is the outliner's hook for future render clients (see
  [`multi-machine.md`](multi-machine.md) §4). Truly unbound outputs get a distinct edge-stub +
  `StatusAttention`; outputs claimed by *other* machines (later) render faded with a machine tag
  instead. This closes the data-model §2.7 UI gap.
- **"Keep the entity, kill its visibility"** (data-model §2.5): outputs are auto-created when content
  or a surface is bound to a plug, display-named after their binding until renamed, and drawn as
  **one merged pill** (`Main Wall → Local / Display 2`) while output↔binding is 1:1 and uncalibrated.
  The OUTPUTS column splits out only when compositing, calibration, an unbound output, or a second
  machine forces the distinction. Binding state appears in both the card label and the outliner edge —
  readiness treatment must hit both.
- Reference images and props: **side shelf** at the right end (compact rows, no flow edges), not flow
  columns.

### B.4 Edges — ✅ first version 2026-09-05 (display + hover only; no scroll stubs, no mapping-stack badges yet)

- Edge set (all from setup data, per data-model §2.1): Slice→Surface (`Surface.SliceId`),
  Slice→Output (`OutputDefinition.SliceId`), Surface→Output (`OutputMappings`), Output→Device
  (`DeviceBinding`). Source→Slice is implicit in the nesting — no line.
- Rendering: short horizontal bezier/orthogonal links in the inter-column gutters, single draw list,
  reference the *patterns* of `MagGraphCanvas.DrawConnection.cs` (not the class). Color: kind color of
  the target, faded by default; full opacity + slight thickening on hover/selection of any endpoint
  (via `SetupRelations`). Deliberately pill-and-column grammar, **not** MagGraph node grammar.
- Rows scrolled out of view: clamp edge endpoints to the column's visible top/bottom with a fade stub.
  One shared vertical scroll region for all columns in v1 (per-column scroll only if real setups
  demand it).
- **Mapping fan-out must read as first-class** (2026-07-29 soft-edge sketch lesson): one surface pill
  with edges to *two* outputs is the canonical soft-edge blend (one surface, one slice, two masked
  mappings — data-model §2.3), and the outliner has to make that path look available, or users will
  rebuild it as duplicated slices + sub-surfaces. Badge the mapping's stack (corner-pin/warp/mask
  icons) at the edge's output end. Two helpers follow: "+ projector" on a surface (drag onto a second
  output → second mapping; both canvas rects back-projected into *surface space*, intersected → fit
  the largest content-parallel band inside the overlap → paired surface-space feather ramps, unity by
  construction — data-model §2.3),
  and — for the *machine-seam* case only — "split for machines" (generates the overlapping slices +
  regions with blend margin so each render client requests only its half; `multi-machine.md` §6).
  Mixed pixel density across a blended seam (1080p + 720p wall) is legal but gets a readiness hint.
- Edge hit-testing/selection: **defer**. When the mapping stack (warp/mask) arrives, mappings become
  selectable (`EntityKind.Mapping` — additive) and edges are their click target; v1 edges are
  display + hover only. Note this in `selection.md` when it lands.

### B.5 Interactions (parity checklist with the old tree)

- Click/ctrl/shift selection through the shared `SetupEntitySelection`; canvas cross-highlight via the
  existing `FrameStats` pulse; click-to-frame on the canvas for off-view entities.
- Drag-to-connect between columns through the existing direction-agnostic `CanConnect`/`ApplyDrop`
  matrix; drop-target states from `states.md`.
- Context menus/rename/delete/duplicate — already shared via `EntityItem.DrawContextMenuItems`.
- Del key on outliner focus deletes selection (through `SetupActions`, snapshot-undoable).

### B.6 Performance

The old panel's lesson (refactoring-plan P3) applies doubled — edges are extra per-frame geometry:

- Layout + edge routing recomputed only on a **structure version tick** (bump in
  `SetupActions.RunUndoable`/save path + on scroll/resize), cached in reusable buffers otherwise.
- No LINQ/closures in the draw loop; label strings cached per row (invalidate on rename);
  `T3Ui.UiScaleFactor` on every literal.

### B.7 Removals in this phase

- `SetupPanel` dies entirely (tree → outliner, cards → Phase A, switcher → strip header). Delete the
  vertical splitter + `_panelWidth` from `OutputSetupModeView`; `DrawSetupPanelMenuItem`/
  `DrawPanelToggleButton` re-target the strip.
- The gutter in/out reference icons die with the tree (replaced by real edges) — remove the
  `Describe*Gutter` paths when porting `SetupRelations`.

**Shippable state after B:** sketch layout achieved — full-width canvas, flow strip below, properties
in the Parameter window.

---

## Phase C — Unified canvas

Today `OutputSetupModeView` routes each selection kind to a different canvas
(`SetupOutputView.Draw`/`DrawSourceCanvas`, `ReferenceImageView`). Target: **one scene** that all
entity kinds live in, with the current views becoming *focus modes* of that scene.

### C.1 Placement model (additive, versioned)

- New optional `CanvasPlacement { Position:Vector2(m), Scale:float }` per content source, output,
  reference image — and per root surface for its *neutral* (unwarped) placement. Serialized additively
  in the setup json (tolerant readers; `Setup.Version` covers it). Placement edits are gesture-undoable
  (`BeginFieldUndo` pattern) and never touch calibration.
- **Seed layout** when absent: kind-grouped columns in meter space (contents left, surfaces center at
  true meter size, outputs right as resolution frames at default px-per-m). Venue-shaped seeding (from
  stage poses) is a later refinement.
- Meters are the canvas unit (settled); content/output items show px rulers/badges at their edges on
  hover/selection — px is the honest unit *inside* a texture. **Physical entities enter at true
  meter proportions; pixel entities (content, outputs) at a default m/px and free-scalable.** Items
  at real scale carry a quiet *true-scale* state; free-scaling drops it — future measuring/annotation
  tools trust only true-scale items (per-item rule, no global "is this board physical?" switch).
- **Free-scaling a pixel entity is board presentation only (2026-08-29):** resizing a content/output
  card changes its board px-per-m and nothing else — never resolution, routing, or projection. It's
  the one card whose handles edit nothing physical; the card should say so (tooltip/status). Content
  cards get **no edge handles** in v1 — edges on content mean *slicing*, reserved for slice editing
  (`canvas-interaction.md` §Edge dragging).
- **Axes & anchors (2026-08-29):** board/stage space is **meters, Y-up, floor at y = 0**; anchors are
  **signed centered** (−1..1, center `(0,0)`, bottom-center `(0,−1)`). Full conventions table:
  [`data-model.md`](data-model.md) §5.
- Live thumbnails on cards ride the existing once-per-frame sink invalidation guard — no double
  content evaluation.
- **Resolution badges follow the `0,0`-auto convention** (data-model §2.5): auto shows the resolved
  value muted, no icon (resolved backwards from the bound display, max-over-consumers on fan-out);
  a **lock icon marks an active override** (pinned value). Typing pins, clearing re-links.

### C.2b Overview card language (settled via the 2026-07-29 sketch)

- **Surface cards use their reference-photo backdrop** (straightened crop via the existing
  `ReferenceBinding` quad + homography) — answers "which wall is this" with data we already have.
  Extension: blend photo ↔ live content on hover/playback.
- **Slices draw as labeled sub-rects inside their content card**; slice edges start at the rect, not
  at an abstract row.
- **Patches draw as labeled sub-rects inside their output card** (2026-08-31) — the mirror of
  slices-in-content. A warped patch shows its quad against the straight canvas rect: the visual that
  teaches "the canvas vs. what lands on it" without UI copy. Reference jigs sit on the board with
  **dashed derivation wires** (glossary); the render path stays solid.
- **Projector ghost frame on mapped surfaces (2026-08-31):** a surface card stays rectified by
  definition, so its mapping's distortion is shown as the *dual*: the output canvas pushed through
  the inverse mapping, drawn as a light warped outline around the straight surface, labeled with the
  output (`→ Display 2`). Its corners are live handles — dragging edits the same
  `OutputMapping.Quad` as the Output camera's surface-quad handles, just from the other end. Shown
  only while the surface (or its mapping) is selected/hovered, per the wires-on-selection rule
  above; its shape alone answers "why does the wall look distorted". This reuses the existing
  Straight-mode machinery (output frame warped around the rectified surface) at neutral placement —
  and it is the mapping's visual body until `EntityKind.Mapping` becomes selectable (Phase B.4).
- **Wires on the canvas: selection/hover path only** — the highlighted chain and the breadcrumb are
  the same object in two forms. (The outliner strip always shows all edges; the canvas never does.)
- **Card label grammar:** name + muted metadata — `4.3×3.2m` (surfaces), `1080p` + binding arrow
  `→ Display 2` (outputs), 🔗-resolution (content/outputs, auto), `(not Ready)` state on content
  whose sink isn't evaluating (power glyph on the card).
- **Metric grid stays subtle** — dotted, tinted toward the surface kind color; never competing with
  photo backdrops; full strength only while dragging/snapping is active.
- **Physical anchors on the board** (2026-07-29 sketch v2): shared guide lines (e.g. `Floor (0 m)`)
  that physical entities align to, and **prop figures** (the existing `Prop`/Person entity) rendered
  at true scale inside/beside surfaces — "everything physical links up": floor line, props, surface
  meter sizes, and reference photos agree, so the board doubles as a sanity check of the venue's
  proportions. **Timing (2026-08-29):** the prop figure appears once the **first surface** exists —
  before that it answers a question nobody asked; from then on it makes the floor-line/meters premise
  self-explanatory. New physical surfaces seed **bottom-aligned to the floor line**, anchor
  bottom-center.

### C.2 Space participation & fading

Each view is a *space* with a transform; entities either participate (drawn distorted through that
space's homography chain) or don't (drawn at neutral placement, faded):

| View | Participates (distorted) | Faded at neutral placement |
|---|---|---|
| **Overview** (new default) | nobody — all neutral | — |
| **Output O** | surfaces mapped to O (warped quads), O's frame, slices routed into O | other surfaces/outputs, unrouted content |
| **Straight (surface S)** | S + coplanar children, rectified (existing morph) | everything else |
| **Content (source T)** | T's texture + its slices (existing) | other sources, surfaces not fed by T |
| **Calibrate (output O)** | as today | rest |

- Participation queries come from `SetupRelations` (same data as the outliner edges — one source of
  truth for "connected").
- The existing `Original→Straight→Content` morph machinery (`_viewMorph`, rectify homography `R`,
  eased framing) **generalizes** rather than being replaced: each entity interpolates between its
  neutral placement and its in-space quad; non-participants interpolate opacity only. This is the 2D
  precursor of the fold — same skeleton, more entities.
- View switching: segmented control in the strip header (Phase B) + double-click an
  output/surface/source on the canvas or outliner to enter its space; Esc / breadcrumb-root returns to
  Overview.

### C.3 Adaptive metric grid

- New `MetricGridRaster` (`Editor/Gui/Windows/OutputSetup/` or next to the canvas helpers): reuse the
  **log-blend spacing math** from `StandardValueRaster.TryGetRastersForScale` (X axis) and
  `HorizontalRaster` (Y axis) — two-axis lines + sparse `m` labels, fading between density levels with
  the existing `fadeFactor` scheme. Don't subclass `AbstractTimeRaster` (it's time/BPM-coupled);
  extract or duplicate the ~30 lines of spacing logic.
- Drawn behind Overview and Straight/Content views (meter or px space respectively — in content space
  the same raster runs in px units). Inside an Output view the output's px raster applies.
- **Snapping:** route grid snapping through the `ICanvasPointSnapper` seam — this resolves the
  refactoring-plan question ("route snapping through it in Phase 4 or delete the seam"): it lives.
  Grid snap is a weak candidate that entity/sibling snaps outrank.

### C.4 Consolidation

- `ReferenceImageView` merges: reference images are placed canvas items; the straighten workflow stays
  as a focused edit mode on the image. (Its `InputTextWithHint` → `AddFilePicker` fix from P4 rides
  along.)
- `OutputSetupModeView.TryDrawEditingView` collapses to: publish focus → draw unified canvas with the
  current space + focus. The per-kind routing switch disappears.
- Selection: canvas picking stays on `CanvasItemPicker` + `SelectionFence`; the sub-element plane
  (`_canvasSelection`) is unaffected. Finish the still-open P2.2 leftovers **before** C:
  `_focusedSurfaceId`/`_selectedSliceId` shadow removal (the unified canvas must read focus from the
  selection, not parallel fields).

### C.5 Later (outline only, not this plan)

Perspective/3D: stage poses (`StagePlacement`) + projector frusta (`ProjectorCamera`) rendered in a
perspective Overview; the 2D neutral placement becomes the *unfolded* pose and the fold transition
interpolates between them — the "unwrapped stage" vision: the board is the paper you cut out and
fold into the stage, and the fold transition (camera path via the view cube + per-entity unfold) is
the teaching device for the whole model. C.1–C.4 are designed so this is additive: placements and
participation rules don't change, only the camera and the interpolation target. Also later and
additive: the freeform thinking layer on the board — notes, sketches, free-pinned material around
the entities (Miro-like; deliberately not in C's scope).

---

## Cross-cutting

- **Undo:** no new command types expected — structural edits stay on `SetupSnapshotCommand`, continuous
  edits on the gesture-snapshot pattern, quad/slice drags on their existing commands. New undoables:
  placement drags (C.1), collapse states explicitly *not* undoable (view state).
- **Docs & tests (repo rules):** each phase updates the manual test set (`.tests-manual/` —
  output-setup walkthrough: re-script property editing via Parameter window in A, outliner
  interactions in B, space switching/fading in C) and the `.help/` output-setup page in the same PR.
- **Multi-window (revised 2026-08-29):** outliner + canvas stay per-window instances; **selection is
  one shared instance** (A.1 / `selection.md` §Selection scope) and each window carries only a pin
  (follow selection ↔ stay on a pinned entity).
- **Refactoring-plan reconciliation:** P2.4 (OutputManager split) unaffected — still before Player
  support. P3 canvas allocation items fold into C. P4 items 2/5 die with `FormInputsNarrow`; item 1
  (`DrawInlineGlyph`) already done; the round-trip serialization tests extend to `CanvasPlacement`.

## Open questions (settle during the phase that touches them)

1. **A:** exact card order for the ContentSource case (op params above vs below the routing card).
2. **B:** DEVICES as a fourth column (sketch) vs binding badges on output rows — column is planned
   (machine-grouped, per `multi-machine.md`); fall back to `Machine / Display` badges on output rows
   if horizontal space proves tight on small windows — the labeling scheme survives either layout.
3. **B:** where the Calibrate mode control lives once the segmented control moves to the strip.
4. **C:** default view after opening a setup — Overview vs last-used space (leaning: last-used,
   Overview on first open).
5. **C:** view-mode consolidation (leaning, 2026-07-29): the segmented control reduces to
   **Board | Stage** — *every other view is a camera, not a mode*. Straight = ortho camera facing the
   selected surface (a surface with world orientation *is* an ortho camera definition); Output = the
   projector's frustum camera ("look through Projector 1", reachable by double-clicking the output);
   Content = ortho onto the source. A **view cube** shown for the selected surface is the transition
   affordance: dragging it tumbles continuously from surface-ortho into the Stage perspective — the
   2D↔3D link, and the rigorous definition of the fold transition (camera interpolation + per-entity
   unfold). Stage rendering can grow from the existing gizmo/floor-grid infrastructure
   (`ITransformGizmoProvider`), eventually binding graph-side 3D scenes to venue reality.
6. **C.5:** fold target for surfaces mapped to multiple outputs / multiple stage instances (primary +
   ghosts — decide before the fold transition is built).
7. **Board (2026-08-31 train notes):** how the output card exposes its patch/mapping handles —
   always visible (makes the mapping evident) vs hidden (exposes inner content, collides with card
   resizing, and distortion is irrelevant for most cases). Candidates: reveal on hotkey
   (`Alt`/`Ctrl`), Figma-style click-again deep select, or an explicit "perspective transform"
   action. Leaning: click-again deep select with the hotkey as accelerator — same decision as
   `canvas-interaction.md` open question 6; settle once.
8. **Warp entry point (2026-08-31):** bezier/lattice warp belongs to the *quad context* — likely a
   context action ("Add warp") on the selected patch/mapping quad, landing in the modifier stack
   (A.3). Settle when warp ships.

## Order

**A → B → C**, each shippable. Inside C: C.3 (grid) is independent and can land first on the existing
canvas; then C.1 placements, C.2 fading/spaces, C.4 consolidation. The P2.2 leftovers (selection
shadows) slot between B and C.
