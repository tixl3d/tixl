# Snapshot control view

A new state of the Parameter window that appears when no child op is selected (the composition itself is active): a detailed control surface for snapshot-affected parameters. Complements the Variations window thumbnails, which stay the visual/blending interface; this view is the precise, per-parameter one.

Related: [`Plan_Sections.md`](Plan_Sections.md) provides the grouping structure (section tree) consumed in Phase C. Phases A and B do **not** depend on it. [`Plan_VariationPicker.md`](Plan_VariationPicker.md) replaces the selector bar's snapshot dropdown with a reusable searchable picker (thumbnails, activation faders, embedded-canvas mode).

## Goal

When the composition is active in a Parameter window, show:

- A **snapshot selector bar**: numeric index indicator, snapshot dropdown, prev/next arrow buttons, and three action buttons — **write** (update snapshot from current values; disabled when values match), **revert** (re-apply the snapshot), **remove**.
- Below, the **controlled ops with their controlled parameters**, editable exactly like the regular parameter view. Clicking an op name selects it and centers it in the graph.
- Composition inputs (already capturable in snapshots via the `Guid.Empty` child id) appear as an **"Inputs" group**.
- Eventually (Phase B): snapshot enablement moves from per-op to **per-parameter**, with a context-menu toggle ("Enable for control") and a green controller icon on controlled parameters.
- Eventually (Phase C, after Sections): ops grouped by their enclosing section, headers showing the nesting path ("Init / Render Quality"), each path segment clickable to center that section.

## Architectural decisions (locked in)

- **List order is derived, not stored.** Ops sort by canvas position (top-to-bottom, then left-to-right). No persisted ordering, no drag-to-reorder in this plan. If derived order proves annoying, reordering becomes a follow-up once sections own member order (see `Plan_Sections.md`, implementation detail on member order).
- **Per-parameter enablement is per child instance**, not per symbol. The existing `IInputUi.ExcludedFromPresets` flag is symbol-level (excludes the input for *all* instances) and serves a different purpose; it stays. The new flag lives on `SymbolUi.Child` as a set of enabled input ids, serialized in the `.t3ui` child entry.
- **Back-compat for the per-op flag:** a child with `SnapshotGroupIndex == 1` and no per-parameter set reads as "all parameters enabled". The graph context-menu toggle ("Enable for snapshots") stays as the bulk on/off convenience and sets/clears all parameters at once.
- **Dirty checking is throttled/cached**, never a naive per-frame compare of every `InputValue` against the variation. Recompute on parameter-change (command execution) plus a low-frequency fallback (e.g. every ~30 frames), cached per snapshot id.
- **All mutations go through commands.** Write/revert/remove and enablement toggles must be undoable (`UndoRedoStack.AddAndExecute`). Reuse `ChangeSnapshotEnabledCommand` patterns; store guids, not instance references.
- **Visual hierarchy** (from the design sketch): view area background `UiColors.WindowBackground`-level fade ~0.5, group background `Button.Fade(0.5)`, input rows `Button`. Use `UiColors.*` + `.Fade()` only — no literal colors. All pixel literals × `T3Ui.UiScaleFactor`.
- **Controller icon**: needs a glyph in the `Icon` enum (atlas-baked). Until it exists, a small filled circle in `UiColors.StatusAutomated`-style green is an acceptable placeholder — flag it for replacement.

## Current state — what exists

- [`Editor/Gui/Interaction/Variations/VariationHandling.cs`](../../Editor/Gui/Interaction/Variations/VariationHandling.cs) — `ActivePoolForSnapshots` / `ActiveInstanceForSnapshots` already resolve to the composition when nothing is selected. `CreateOrUpdateSnapshotVariation()` and `AddSnapshotEnabledChildrenToList()` iterate per-op `EnabledForSnapshots`.
- [`Editor/Gui/Interaction/Variations/Model/Variation.cs`](../../Editor/Gui/Interaction/Variations/Model/Variation.cs) — `ParameterSetsForChildIds` is already `childId → inputId → value`, so the *file format is per-parameter today*; composition inputs use key `Guid.Empty`. `States` enum already has `Modified`.
- [`Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs`](../../Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs) — `Apply`, `BeginBlendTowardsSnapshot`, `TryCreateVariationForCompositionInstances`, `UpdateVariationPropertiesForInstances`, `DeleteVariation`, `SaveVariationsToFile`.
- [`Editor/UiModel/SymbolUi.Child.cs`](../../Editor/UiModel/SymbolUi.Child.cs) — `SnapshotGroupIndex` backing field; `EnabledForSnapshots` property. Serialized in [`Editor/UiModel/SymbolUiJson.cs`](../../Editor/UiModel/SymbolUiJson.cs).
- [`Editor/UiModel/Commands/Graph/ChangeSnapshotEnabledCommand.cs`](../../Editor/UiModel/Commands/Graph/ChangeSnapshotEnabledCommand.cs) — undoable per-op toggle; pattern reference for the per-parameter variant.
- [`Editor/Gui/Windows/ParameterWindow.cs`](../../Editor/Gui/Windows/ParameterWindow.cs) — `ViewModes` enum (Parameters/Settings/Help); the no-selection branch currently calls `DrawSettingsForSelectedAnnotations()` and returns — this is the insertion point. Static `DrawParameters()` renders editable input rows and is reusable.
- [`Editor/Gui/Windows/ParameterSettings.cs`](../../Editor/Gui/Windows/ParameterSettings.cs) — reference for row styling and (later) drag-handle interaction.
- [`Editor/UiModel/InputsAndTypes/InputValueUi.cs`](../../Editor/UiModel/InputsAndTypes/InputValueUi.cs) — parameter context menu (`CustomComponents.ContextMenuForItem`), insertion point for "Enable for control".
- [`Editor/Gui/MagGraph/Interaction/GraphContextMenu.cs`](../../Editor/Gui/MagGraph/Interaction/GraphContextMenu.cs) — per-op "Enable for snapshots" toggle (stays, becomes bulk toggle).
- [`Editor/Gui/Windows/Variations/VariationsWindow.cs`](../../Editor/Gui/Windows/Variations/VariationsWindow.cs) + `SnapshotCanvas.cs` — the other UI mutating the same pool; must stay in sync (activation state, thumbnails) when this view writes/removes.
- Centering an op in the graph: see `ProjectView` / `NodeSelection` fit-view handling used by existing "center on selection" interactions.

## Phases

### Phase A — Snapshot control view with per-op granularity

**Status (2026-06-13): implemented** — `Editor/Gui/Windows/SnapshotControlView.cs`, wired into `ParameterWindow`'s no-selection branch. Test set: [`snapshot-control-view.md`](../../.tests-manual/snapshot-control-view.md). Controller icon and per-parameter enablement remain for Phase B.

**Goal:** the view ships and is useful with the *existing* data model. No serialization changes.

**Scope:**

1. New view state in `ParameterWindow`. In the no-selection branch: if `VariationHandling.ActivePoolForSnapshots != null`, draw the snapshot control view (annotation settings remain reachable when an annotation is explicitly selected). Each window instance keeps its own scroll state; the pool is global per focused composition.
2. **Selector bar** (top, fixed): index indicator (`ActivationIndex`), dropdown listing snapshots (title + index; fall back to "Snapshot #n"), prev/next arrows cycling by `ActivationIndex` order, then right-aligned via `CustomComponents.RightAlign`: write / revert / remove icon buttons. Write + revert enable only when the dirty check reports modification; remove always enabled when a snapshot is selected.
   - Write = `UpdateVariationPropertiesForInstances` (existing undoable path), not delete + recreate, so id/thumbnail position survive.
   - Revert = `Apply` of the selected variation (already command-based).
   - Empty pool → `CustomComponents.EmptyWindowMessage` with a hint + "create snapshot" button (`CreateOrUpdateSnapshotVariation`).
3. **Op list** (flat, this phase): all children with `EnabledForSnapshots`, sorted by canvas position (top-to-bottom, left-to-right). Per op: a header row (op name, colored by type like the graph; click → select + center in graph; **not collapsible** — collapsing is reserved for the section groups in Phase C) and below it the parameter rows for the inputs captured in the selected snapshot, rendered with the existing `DrawParameters` row machinery so editing, context menus, and animation indicators behave identically.
   - Ops present in the snapshot but no longer enabled (or deleted) → show a muted "stale" row with a cleanup affordance (`RemoveInstancesFromVariationsCommand` exists).
4. **Inputs group**: if the snapshot contains the `Guid.Empty` set, list composition inputs as a group titled "Inputs".
5. **Dirty check**: small helper class (e.g. `SnapshotModificationCheck`) comparing current instance values against the selected variation's `InputValue`s — event-driven via command execution plus throttled fallback; no per-frame allocations (reuse buffers, no LINQ).
6. Sync with Variations window: after write/remove, the pool's variation list and `State` flags must reflect immediately (both UIs read the same `SymbolVariationPool`; verify `UpdateActiveStateForVariation` paths).

**Out of scope for A:** grouping, reordering, per-parameter enablement.

**Verification:** manual test set under `.tests-manual/` (create/select/modify/write/revert/remove; undo each; check Variations window stays consistent; check lib-namespace compositions show no view since `ActivePoolForSnapshots` is null there).

### Phase B — Per-parameter enablement

**Status (2026-06-13): implemented** — `SymbolUi.Child.SnapshotEnabledInputIds` (null = all, legacy), `ChangeSnapshotEnabledInputsCommand`, capture/apply/blend filtering in `SymbolVariationPool`, "Enable for control" context-menu toggle via `VariationHandling.ToggleParameterSnapshotControl`. Controller indicator: `Icon.Knob` tinted `UiColors.StatusControlled` in the input connection area. The view is suppressed during SkillQuest play mode.

**Per-parameter actions menu (2026-06-14):** each parameter row has a `…` (`Icon.Settings2`) actions button in the right gutter beside the revert icon (gutter widened to two icons). Items: **Write to snapshot** (enabled when modified), **Write to all snapshots** (enabled when it differs from any — compared only while the popup is open), **Reset** (to default), `---`, **Disable Snapshot control** (ops only; composition `Guid.Empty` inputs can't toggle through this child-ui). Writes reuse `VariationHandling.ApplyParameterToVariations` (built on the existing `CloneParameterSetsWithValue` + `UpdateVariationParametersCommand`), one undoable macro each.

**Snapshot actions in the parameter right-click menu (2026-06-14):** the snapshot write/reset actions are shared via `SnapshotControlView.DrawSnapshotActionMenuItems` (Write to snapshot, Write to all snapshots, Reset to Snapshot) — a `static` method living in the control-view class (kept out of the already-long `InputValueUi`), gated on its private static `_activeSnapshotMenuContext` (a `{pool, activeSnapshot, childKey}` record the view sets around `DrawControlledParameters`, null elsewhere). `InputValueUi`'s right-click menu calls it (it already references `T3.Editor.Gui.Windows`); `IsSnapshotControllable` / the "Control with Snapshots" toggle stay in `InputArea`. Both the per-row gear menu **and** the parameter right-click context menu call it; in the right-click menu they sit under a new **"Snapshot control"** group label (next to "Control with Snapshots"). "Reset" was relabeled **"Reset to Default"** to distinguish it from **"Reset to Snapshot"** (the menu form of the revert icon). For the composition `Guid.Empty` inputs, the snapshot lookup uses the context's `ChildKey` while the reset-to-snapshot command uses the in-scope `compositionSymbol`/`symbolChildUi.Id` — consistent because the composition's input is stored under `Guid.Empty` in its own pool.

**Menu + nav refinements (2026-06-14):** `CustomComponents.DrawMenuItem` gained `reserveCheckmarkColumn` — the snapshot menus (selector-bar + per-param) have no toggles, so they drop the checkmark column and sit further left. **Double-clicking the picker trigger** renames the active snapshot (`VariationPicker.Draw` reports it via `out renameRequested`; the popup that briefly opened on the first click closes once rename mode replaces the picker). **Left/Right arrows** cycle snapshots and **Enter** renames the active one while the parameter window holds focus (`IsWindowFocused(RootAndChildWindows)`, gated on `!IsAnyItemActive && !WantTextInput`), with the shared focus frame drawn around the window. Enter-rename is collision-free because the view only shows when nothing is selected in the graph (so the graph's own Enter-rename is inert). The prev/next bar-arrow tooltips name the shortcut.

**Goal:** "enabled for snapshots" becomes a per-parameter property of the child instance.

**Scope:**

1. Data: `SymbolUi.Child` gains a serialized set of snapshot-enabled input ids (e.g. `HashSet<Guid> SnapshotEnabledInputIds`). Reader back-compat: `SnapshotGroupIndex == 1` with an absent/empty set → all inputs enabled (materialize lazily, don't write until the user changes something). Writer keeps `SnapshotGroupIndex` for ParameterCollections (>1) untouched.
2. New undoable command `ChangeSnapshotEnabledInputsCommand` (guid-based, defensive resolution per the undo/redo rules in `AGENT_INSTRUCTIONS.md`).
3. `VariationHandling` / `SymbolVariationPool.TryCreateVariationForCompositionInstances` filter captured inputs by the per-parameter set (today they capture all non-default, non-`ExcludedFromPresets` inputs of enabled ops).
4. Parameter context menu (`InputValueUi`): add "Enable for control" toggle alongside animate / extract / publish-as-input. Controlled params get the green controller icon next to the name (new `Icon` enum glyph — ask for one; placeholder circle until then).
5. Graph context menu per-op toggle becomes bulk: enable = all params, disable = clear set. Existing auto-update of snapshots on toggle (`GraphContextMenu` behavior) extends to parameter-level changes.
6. Snapshot control view: enabling/disabling a parameter updates the listed rows; per-op "stale" handling from Phase A covers params removed from control.
7. Decide and document interplay with symbol-level `ExcludedFromPresets` (exclusion wins; the control view never offers excluded inputs).

**Verification:** extend manual test set; specifically test old `.t3ui` files (per-op flag only) load with all-params semantics and don't rewrite files on mere load.

### Phase C — Section grouping (depends on `Plan_Sections.md` Phase 1)

**Status (2026-07-05): implemented** — the op list consumes `SectionTree.Build` (its first consumer) via a flattened row list (`DisplayRow`), rebuilt on undo-stack/section-count/op-count changes plus a 30-frame fallback (same throttling idiom as the `ModificationCheck`). Design deviations from the scope below:

- **Path headers as originally sketched** ("TEST01 / SUBSECTION"), small muted all-caps, no indentation — an indented-tree variant was tried first but consumed too much row width. One group per section that *directly* contains snapshot-enabled ops; sections without direct ops leave no header, their name appears only in descendants' paths. Sections whose subtree holds no snapshot-enabled op are skipped entirely.
- Collapse state per window instance (`HashSet<Guid>` in the view), not serialized, independent of the frame's collapse state in the graph. Collapsing hides only the group's own ops — nested groups keep their own headers and state.
- Header tools right-aligned: **Aim icon** centers the frame via `IGraphView.OpenAndFocusSection` (selection-free — selecting a `Section` would flip the parameter window to section settings), **Reset icon** reverts the group's own ops to the snapshot (one macro; enabled via the dirty-cache's new `IsAnyInputModifiedForChild`).
- Ops hidden in a collapsed graph frame stay listed; clicking their name jumps to the collapsed frame (`HiddenInCollapsedSectionId`) instead of selecting the invisible op.
- "Ungrouped" bucket (pseudo-header, `Guid.Empty` collapse key) only appears when at least one real group exists; without sections the list stays flat as before.
- Ordering switched to top-to-bottom / left-to-right (ascending) for groups and ops alike, matching `SectionTree`'s member order — this retires the "bottom-right first" ordering experiment.
- Drag-to-reorder (stretch goal) not done — sections still don't own a persisted member order.

**Name-click reverts to snapshot (2026-07-05):** in the control view, clicking a parameter name now reverts to the *active snapshot's* value (what the row highlight compares against) instead of the default — `SnapshotControlView.TryResetParameterToSnapshot`, hooked into `InputValueUi`'s name-click and gated on the same `_activeSnapshotMenuContext`; the hover hint/revert icon follow (`IsResetToSnapshotActive` + `DimHighlightOverride`), showing only when the row differs from the snapshot, and a matching value is a no-op (no undo entry). This made the per-row revert icon redundant, so it was removed together with its drag-to-scale infinity slider (`HandleRevertHandle`) — the right gutter is back to a single icon (the `…` actions menu), saving row width. "Reset to Snapshot" stays available in the menus.

**Goal:** ops grouped under their enclosing section, with nesting paths.

**Scope:**

1. Consume the section tree helper (built in the Sections plan) — never re-derive membership geometrically here.
2. Group headers: innermost section per op; header shows the nesting path ("Init / Render Quality"); each segment clickable → center that section in the graph. Ops without a section fall into "Ungrouped" at the bottom; composition inputs stay the "Inputs" group at top.
3. Groups collapsible (collapse state per window instance, not serialized). Group-level revert button: re-applies the snapshot values for ops in that group only, enabled when any contained parameter is dirty (reuse the dirty-check cache).
4. Group order derived from section canvas position. Manual reordering of groups/ops via the drag-handle interaction (`ParameterSettings` pattern) is a *stretch goal*, only if sections own a persisted member order by then.
5. Ops hidden in a collapsed section still appear; clicking them expands/centers on the collapsed frame.

## Controller-index grid

Clicking the index left of the selector dropdown opens a launchpad-style grid of activation indices. It makes the MIDI index layout explicit and visually distinct from the list's display order, addressing the index-vs-order confusion.

- **Done (2026-06-14):** grid view (`DrawControllerGrid`) with click-to-apply, hover preview (gated on `VariationHoverPreview`) + title tooltip.
- **Done (2026-06-14): pluggable layouts.** [`ControllerGridLayout`](../../Editor/Gui/Interaction/Midi/ControllerGridLayout.cs) maps a screen cell → activation index; `CompatibleMidiDevice.GridLayout` lets each controller surface its own arrangement (it already knows this mapping for LED colors — `ApcMini` returns its bottom-up 8×8). `ControllerGridLayouts.All` collects them (reflection over the device scan) plus a built-in **"Reading order"** (top-down), which is now the **default** — fixing the counter-intuitive bottom-up. The grid popup has a layout dropdown when more than one exists.
- **Fixed (2026-06-14): APC off-by-one.** The APC layout is **0-based** (raw pad note, `(7-row)*8 + col`, no `+1`): the APC's pads are notes 0–63 and `ButtonRange.GetMappedIndex` returns `note - startIndex` (= the note), so the snapshot index a pad activates equals its note. The earlier `+ 1` shifted every cell one pad off the hardware. (Reading order stays 1-based / natural; the cell label is always the activation index it represents.)
- **Done (2026-06-14): drag-to-reassign index.** Dragging a populated cell reassigns its `ActivationIndex` via [`ChangeVariationActivationIndexCommand`](../../Editor/UiModel/Commands/Variations/ChangeVariationActivationIndexCommand.cs): drop on a free slot **moves**, drop on an occupied slot **swaps** (the occupant takes the dragged cell's old index — never two snapshots on one pad). Drag is tracked manually (`_gridDragSourceId`, mouse-over-cell hit test) rather than via ImGui's payload API; the source cell dims **via a scrim** (uniform for active and inactive cells — alpha-fading the colors made muted cells vanish), the target outlines, and a cursor chip carries the index **and name**. Hover-preview / tooltip / click-to-apply stand down during a drag; releasing on the source or outside is a no-op.
- **Done (2026-06-14): header + hover polish.** Grid header title is `FontNormal`, vertically centered against the dropdown row, with a `DocumentationButton` to the right of the layout dropdown. Op-block hover (`EndBlock`) is now gated on `ImGui.IsWindowHovered` so an open popup (grid, picker, actions menu) no longer bleeds hover onto the panels behind it — `IsMouseHoveringRect` alone is purely geometric and ignored the popup on top.
- **Done (2026-06-14): extracted to own class.** The whole popup now lives in [`SnapshotControllerGrid`](../../Editor/Gui/Windows/SnapshotControllerGrid.cs) (its own popup id, layout/preview/drag state, and helpers). `SnapshotControlView` calls `_controllerGrid.Open(pos)` from the index label and `_controllerGrid.Draw(pool, composition, active, snapshots)` each frame, applying the returned snapshot if one was clicked.
- **Done (2026-06-14): affordance + help.** Title is now "Edit controller index"; filled cells show a hand cursor (and the drag carries it) to signal drag-to-reassign. Doc button is flush-right (manual cursor X — `RightAlign` inset it by an extra window padding). The doc button now points at a focused [`.help/embedded/ControllerIndex.md`](../../.help/embedded/ControllerIndex.md) ("what is this panel") instead of the broad Variations snippet.
- **Done (2026-06-14): cell colors mirror the APC.** Filled cells are **green** (`StatusControlled` — a controllable snapshot lives there; resting at `.Fade(0.7f)`, full on hover) and the active/live cell is **magenta** (`StatusAttention`), matching the APC Mini's green-used / hot-active LEDs and keeping "green = controlled" intact (the grid was the only place painting the *active* cell green; the Variations window already marks active with the orange `WidgetActiveLine` dot). Drop-target outline switched to `ForegroundFull` so it reads against a green cell. Filled cells use the `ResizeAll` (move) cursor, not a pointing hand, to read as draggable.
- **Done (2026-06-14): index button.** The selector-bar index is now a rounded button (hover-lit) with the zero-padded index, tooltip "Click to edit MIDI controller indices". `UpdateCachedLabels` also keys on `ActivationIndex` so reassigning the active snapshot's index in the grid refreshes the button label (was cached on id/title only).
- **Done (2026-06-14): + always enabled.** The create-snapshot `+` is always clickable now — `Default` state normally, `Emphasized` when the active snapshot has unsaved changes (was disabled-when-unchanged).
- **Done (2026-06-14): bar Write, menu Revert.** The selector bar's icon is now **Write** (`Icon.Apply`, emphasized when modified → `WriteSnapshot`); **Revert** moved into the actions menu (`Icon.Reset` → re-apply the snapshot). The old bar-revert infinity-slider (`DrawBarRevertButton` + `_barRevert*`) was removed — the user never used the drag-to-scale; the per-row revert icon (with its slider) is untouched.
- **Done (2026-06-14): insert behind active.** Creating a snapshot while one is active gives the new one the next free controller index above it (`SymbolVariationPool.GetNextFreeActivationIndexAfter`, ignoring the just-created variation) and inserts it **behind the active one on the canvas with no overlap/gap**: `InsertSnapshotBehind` parks it at the next free slot, then bubbles it back through reading order — each swap shifts a following snapshot one slot later (the picker's reorder mechanic). Reading-order sort extracted to the shared `VariationBaseCanvas.SortByReadingOrder` (reused by the picker). No active snapshot / explicit MIDI slot still uses `FindFreePositionForNewThumbnail`. (Undo caveat: the position shift isn't reverted on undoing the create — positions on create were never command-backed — so a mid-list insert+undo leaves a one-slot gap; only matters when the active wasn't already last.)
- **Done (2026-06-14): settings menu + thumbnail cells.** The header now has a `Settings2` gear (left of the doc button) opening a settings popup; the layout/device selection moved there from the header combo (checkable list), plus a **Show thumbnails** toggle. With thumbnails on, cells draw the snapshot thumbnail (via `ThumbnailManager`) and the green/magenta state moves to the cell **border**, the index sitting on a dark backing for legibility. `_showThumbnails` is session-only.
- **Done (2026-06-14): layout persisted.** The chosen layout is saved in `UserSettings.Config.SnapshotControllerLayout` **by name** (the reflection-built `ControllerGridLayouts.All` index isn't stable across runs); `ResolveLayoutIndex` maps it back, falling back to the first ("Reading order"). `UserSettings.Save()` on pick.
- **Deferred:** persisting the show-thumbnails preference (session-only) and a per-device layout for the non-APC controllers.
- **Done (2026-06-16): unified snapshot order on the controller index.** Dropped the separate canvas-position "reading order" as the sort source — snapshots now sort by `ActivationIndex` everywhere they're listed (picker, modulo-activation, BlendSnapshots ordinal mode) via the renamed `VariationBaseCanvas.SortByActivationIndex`, so the list, the controller grid and the MIDI pads share one order. Picker drag-reorder now swaps `ActivationIndex` (`SwapActivationIndices`), committed via the new `ModifyVariationIndicesCommand` (mirrors `ModifyCanvasElementsCommand` for the index). Insert-behind-active simplified to just `GetNextFreeActivationIndexAfter` — the index alone places it, so the `InsertSnapshotBehind` PosOnCanvas bubble was removed (and with it the undo caveat above). The Variations-window 2D canvas keeps free `PosOnCanvas` placement for Alt-drag spatial blending — only the *sort source* changed, not the canvas.

## Open questions

- Should the snapshot control view *also* replace the regular no-selection state when the composition has published inputs (the "this whole view could be the parameter window" idea)? Deferred until the view has proven itself — it changes what "nothing selected" means for every user.
- Prev/next arrows: cycle by `ActivationIndex` or by list order in the pool? (Proposal assumes activation index; gaps are skipped.)
- Should "write" while a *blend* is active commit the blended state? Probably yes (it equals current values), but verify against `BlendActions.SmoothVariationBlending`.

## Documentation

- New page or section under `.help/using/` for snapshots/control view in the same PR as Phase A; extend for B and C.
