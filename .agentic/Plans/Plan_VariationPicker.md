# Variation picker

A reusable variation/preset/snapshot selector that replaces the plain combo dropdown in the
snapshot control view ([`Plan_SnapshotControlView.md`](Plan_SnapshotControlView.md)) with a richer
custom widget: searchable list with thumbnails and per-row activation faders, plus an embedded
canvas mode. Built generic over a pool so the same widget can later sit next to the instance
title as a **preset** selector.

## Goal

Replace `ImGui.BeginCombo` for snapshots with `VariationPicker(pool, instance)` — a popup that:

- Opens from the selector bar (and later from a preset chip next to the instance title).
- Has two view **modes**, toggled by icons in its top row:
  - **List** (default): one row per variation — thumbnail, index, title, activation fader.
  - **Canvas**: the existing `SnapshotCanvas` / `PresetCanvas` (`VariationBaseCanvas`) embedded in the popup.
- A **search** field (focus-after-open, filter by title), styled like `InputWithTypeAheadSearch`.
- A **hover-preview** toggle next to the mode toggles (reuses `VariationHoverPreview`).
- Rows have **active / hover / normal** states.

## Architectural decisions (locked in)

- **One reusable component.** `VariationPicker` takes a `SymbolVariationPool` + composition
  `Instance` (+ a kind hint, preset vs snapshot). The snapshot control view is its first host;
  the preset selector adopts it later. No snapshot-specific logic in the widget.
- **List order is derived from canvas position, row-banded.** Variations already carry
  `PosOnCanvas` (their slot in the 3-column auto-layout grid from
  `VariationBaseCanvas.FindFreePositionForNewThumbnail`). Order = quantize Y into a row index via
  the grid step (`ThumbnailSize.Y + SnapPadding.Y`), then sort by X within the row. A raw
  lexicographic `(Y,X)` sort misorders a wrapped grid the moment row Ys differ slightly — band
  into rows first. Reuse the layout's own step/padding constants so list order and canvas layout
  can't drift.
- **`ActivationIndex` stays purely semantic.** It's the MIDI-controller LED slot (intentionally
  sparse / non-continuous); it is *not* the list order. Display order is canvas position only.
- **Drag-to-reorder repositions on the canvas.** Dragging a list row updates its `PosOnCanvas`
  (undoable, mirroring the canvas thumbnail drag), so list and canvas modes share one order. No
  separate persisted order field.
- **Activation faders are per-gesture blend-toward-variation, undoable per release.** A fader
  drag blends the *current* state toward that variation (`blend(current, V, t)`), reusing the
  pool's existing blend paths (`BeginBlendTowardsSnapshot` for snapshots, `BeginBlendToPresent`
  for presets) and the infinity-slider interaction. On mouse-up it bakes the result as **one**
  `MacroCommand` of `ChangeInputValueCommand`s and pushes it to the undo stack. Consequences:
  - Mixing is **sequential/additive**: "mix in red, then blue, then magenta" each blends from
    what's already there. Undo peels off exactly one mix-in — matching the user's mental model.
  - `t < 0` subtracts (extrapolates); the fader bar fills from the right with
    `UiColors.StatusAttention`. `t > 1` over-drives.
  - **No weight vector lives on the undo stack** — commands stay pure parameter mutations
    (guids + value snapshots), reload-safe, per the undo rules in `AGENT_INSTRUCTIONS.md`. What
    the user perceives as "my mix" is the resulting parameter state, which is exactly what each
    command restores.
  - To *reduce* an earlier ingredient you re-drag it toward subtract from the current state, not
    "pull a held fader back down."
- **The live simultaneous console is a separate, optional future mode**, not the core. Holding a
  persistent weight vector across several variations (`BeginWeightedBlend`) is the only place
  weights would be session state; defer until performing shows it's needed.
- **Visual:** transparent tool icons + `ButtonStates` per the tool-icon convention; status hues
  per the legend (green = controllable, magenta = attention, etc.).

## Current state — what exists

- [`Editor/Gui/Windows/SnapshotControlView.cs`](../../Editor/Gui/Windows/SnapshotControlView.cs) — the combo to replace; already has row-banding-adjacent sort, the revert infinity-slider gesture, and the per-row blend plumbing to generalize.
- [`Editor/Gui/Windows/Variations/VariationBaseCanvas.cs`](../../Editor/Gui/Windows/Variations/VariationBaseCanvas.cs) (+ `SnapshotCanvas` / `PresetCanvas`) — the embeddable canvas mode; `FindFreePositionForNewThumbnail`, `ThumbnailSize`, `SnapPadding`.
- [`Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs`](../../Editor/Gui/Interaction/Variations/Model/SymbolVariationPool.cs) — `Apply`, `BeginBlendTowardsSnapshot`, `BeginBlendToPresent`, `BeginWeightedBlend`, `ApplyCurrentBlend`, `StopHover`, `SetActiveVariationWithoutApply`.
- [`Editor/Gui/Interaction/Variations/VariationThumbnailRenderer.cs`](../../Editor/Gui/Interaction/Variations/VariationThumbnailRenderer.cs) — per-row thumbnails without the Variations window.
- `InputWithTypeAheadSearch` — search field reference (used in the parameter header).
- `ThumbnailManager.GetThumbnail` / `AtlasSrv` — thumbnail draw source.

## Phases (each shippable)

1. **List-mode picker, read-only.** ✅ **Done (2026-06-13).**
   [`VariationPicker`](../../Editor/Gui/Windows/Variations/VariationPicker.cs) — combo-style
   trigger + popup with auto-focused search, row-banded canvas order, rows
   (thumbnail + index + title) with hover/active states, click-to-apply. Replaces the snapshot
   selector-bar dropdown. (PopupId is a shared const for now — give it a per-instance id before
   mounting a second picker in the same window.)
2. **Mode toggle + hover preview.** ✅ **Done (2026-06-13).** Top-row `ViewList` / `ViewGrid`
   toggles switch list ↔ embedded canvas; `HoverScrub` toggle bound to `VariationHoverPreview`.
   Hover preview applies the *highlighted* variation via `pool.BeginHover`/`StopHover` (restored
   on change/close). Canvas mode embeds a caller-passed `VariationBaseCanvas`
   (`SnapshotControlView` owns a `SnapshotCanvas`). **Known limitation (confirmed):** the embedded
   canvas is a second `SnapshotCanvas` on the same pool, sharing the pinned output + the static
   hover state in `VariationThumbnail`. When the Variations window is *also* open the two contend
   over the output slot, so canvas-mode live preview doesn't update; it works with that window
   closed. Decision (2026-06-13): leave as-is, documented via the canvas-toggle tooltip. If it
   becomes annoying, the clean fix is a picker-drawn thumbnail grid with hover preview through
   `pool.BeginHover` (no second canvas, no shared state) — preferred over refactoring `DrawBaseCanvas`.
3. **Drag-to-reorder.** ✅ **Done (2026-06-14).** Whole-row drag swaps the dragged variation's
   `PosOnCanvas` with its neighbor as the mouse crosses each row (classic ImGui swap-on-drag),
   committed once on release as a `ModifyCanvasElementsCommand` (undoable) + `SaveVariationsToFile`.
   A grip is drawn in a reserved left strip on the highlighted row. Enabled only when the picker
   has a canvas (the move's selection container) and the search is empty; a drag suppresses the
   row's click-to-apply. Shares order with the canvas since both read `PosOnCanvas`.
4. **Activation faders.** Per-row blend-toward-variation infinity-slider gesture, bake-on-release
   as one undoable command; negative-subtract with attention fill; sequential mixing.
5. **Preset reuse.** Mount the same `VariationPicker` as a preset chip next to the instance title
   in the parameter window header.

## Future ideas (out of scope here)

- **Live console mode**: persistent weight vector + `BeginWeightedBlend`, ride several faders at
  once. Session-only weights; bake on demand.
- **t-SNE landscape**: a third canvas mode laying thumbnails out by parameter-set similarity for
  spatial blending. Needs a similarity metric + dimensionality reduction — a separate research
  spike, not this widget's first life.

## Open questions

- Reorder semantics when the grid is full: does repositioning re-flow the whole 3-column layout,
  or just swap two slots? (Lean: insert + re-pack via the existing auto-layout.)
- Fader "rest" display: active variation reads 100%, others 0% — accept that it shows intent, not
  a measured match when parameters were hand-edited (the per-parameter rows already show deltas).
- Should search also match `ActivationIndex` / op names captured in the variation, or title only?

## Documentation

- Update [`PresetsAndSnapshots.md`](../../.help/docs/using/PresetsAndSnapshots.md) and the snapshot
  control-view test set as each phase lands.
