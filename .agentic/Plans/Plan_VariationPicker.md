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
- **Activation faders are a normalized cross-fade over a persistent weight vector** (refined
  2026-06-14; supersedes the earlier per-gesture blend-toward model). The primary use is *slowly
  cross-fading* one variation into another, not free mixing. The picker holds a live
  `weights: variationId → float` vector while it's open, applied via `pool.BeginWeightedBlend`.
  - **Normalized (default):** weights sum to 1. Dragging row R to `w` distributes `1-w` across the
    **other faders in their ratio as captured at drag-start** (`BeginBlendWeightDrag`), not their
    current values. A simple A→B fade gives the 95/5 example; 3+ ingredients keep their relative
    balance — and crucially the fade is **reversible within the gesture**: drag R to 100% (others →
    0) and back down and the sources refill from the captured ratio instead of stranding at 0.
    Per-slider clamp at 100% completes the fade; pulling the old one to 0 does the same. While a
    fader drags, its drag-start sources are flagged (faint accent border) so the blend-back target
    is visible.
  - **CTRL = free mode:** drop the sum constraint; weights move independently and may exceed 100%
    (over-drive / extrapolate). Indicated while held.
  - **SHIFT = ⅓-speed fine drag.** Active slider draws blue (border + fill + text). Reorder only via
    the left grip (see row-region split below).
  - **Edge:** the sole non-zero (active) row, when idle, can't be dragged below 100% — there's
    nowhere to send the weight until another row is raised (it shows a `NotAllowed` cursor +
    tooltip). Once a drag is in flight the drag-start ratio supplies the blend-back target, so
    dragging down during the gesture works.
- **`WeightedBlendMethods` is a raw weighted *sum*, not internally normalized**
  ([`Core/Utils/ValueUtils.cs`](../../Core/Utils/ValueUtils.cs)). So the UI must (a) keep weights
  summing to 1 in normalized mode and (b) **always pass the base variation** in the list — else
  dragging a row to 0.1 yields 10% of its own values over defaults, not a cross-fade. In free mode
  the raw sum is exactly what produces the >100% over-drive.
- **Blend lifecycle / undo.** While a weight drag changes the vector, call `ApplyBlendWeights`
  each frame (it re-runs `BeginWeightedBlend`, auto-reverting the prior uncommitted blend). On
  mouse-up, `ApplyCurrentBlend` bakes the result as **one** `MacroCommand` on the undo stack. The
  undo stack holds only pure parameter mutations (guids + value snapshots), reload-safe per
  `AGENT_INSTRUCTIONS.md`. Suppress the hover-preview loop (`BeginHover`) while dragging — both
  share `_activeBlendCommand`.
- **The weight vector is pool-owned session state, not transient picker state** (revised
  2026-06-14). It lives on `SymbolVariationPool` (`_blendWeights`, with `GetBlendWeight` /
  `SetBlendWeight` / `ResetBlendWeights` / `ApplyBlendWeights` / `GetDominantBlendVariation`) so it
  **persists across picker opens** and stays coherent with activations from the arrows, controller
  grid and MIDI. The weights are the *source of truth*; parameters are their rendered output — so a
  manual parameter edit (or an undo) is just an override the next blend overwrites, and **does not**
  invalidate the vector. Invalidation rules: **activating a single variation** (any surface — it
  flows through the `ActiveVariation` setter) resets it to `{that: 1}`; **removing a variation**
  drops its entry and renormalizes the rest; a **new pool** (composition switch / reload) starts
  empty. The picker only seeds it (`ResetBlendWeights`) when the pool has none yet.
- **Selector bar mid-mix** follows `GetDominantBlendVariation()` — for a plain activation the vector
  is `{active:1}` so that's just the active; during a fade it tracks whichever ingredient dominates.
- **Row-region split (prerequisite).** The row currently reorders on a whole-row drag; the grip is
  only drawn. Split the row into **left grip = reorder**, **middle = click-to-apply**, **right
  weight cell = fader drag** (the weight cell is a second `InvisibleButton` emitted after the row
  button so it wins hit-testing; reorder is gated to a mouse-down within the grip strip).
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
4. **Activation faders (normalized cross-fade).** Right-cell weight slider per row over a live
   weight vector; proportional normalize (default) / CTRL free over-drive / SHIFT fine; blue active
   highlight; live `BeginWeightedBlend` → `ApplyCurrentBlend` bake on release; row-region split with
   grip-gated reorder. See the decisions above.
5. **Preset reuse.** Mount the same `VariationPicker` as a preset chip next to the instance title
   in the parameter window header.

## Future ideas (out of scope here)

- **Variations-window fader view** (user idea, 2026-06-14): host the list+faders as a third view
  mode in the Variations window alongside canvas/grid. Cheap now that the weight vector is
  pool-owned (shared state, no divergence) and the row drawing is already factored — the work is
  extracting the list into an *inline* panel (not popup-hosted) + a view toggle. Fits the
  architecture (the Variations window is the designated blending interface) and avoids the
  embedded-canvas pinned-output contention since a fader list drives the real blend. Overlaps with
  phase 5 (same extraction).
- **Live weight on the Variations thumbnails** (user idea, 2026-06-14, "love that"): two levels.
  - *(a) Display* — ✅ **Done (2026-06-14).** Thumbnails overlay their live `pool.GetBlendWeight(id)`
    via the existing `VariationThumbnail.DrawBlendIndicator`, gated on `SymbolVariationPool.IsLiveBlendMix`
    (2+ non-zero weights, so the resting `{active:1}` stays unannotated). `VariationBaseCanvas.TryGetLiveBlendWeight`
    bridges canvas → pool; yields to the canvas's own fence/ALT blend gestures.
  - *(b) Interactive* — thumbnails become faders (scrub to set weight via the same
    `BeginBlendWeightDrag`/`SetBlendWeight` path), which needs disentangling from the thumbnail's
    existing click-to-apply and drag-to-reposition. Not started.
- **Live console mode** — largely realized by the phase-4 fader list (persistent pool-owned weights,
  `BeginWeightedBlend`). Remaining: a dedicated "ride several at once" performance layout if needed.
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
