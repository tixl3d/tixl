# Click an empty controller slot to create a snapshot

Ticket: #1079 — https://github.com/tixl3d/tixl/issues/1079
Size: —   Milestone: v4.2

## Problem
In the controller index grid, clicking an *empty* slot currently does nothing. The idea: clicking an
empty slot creates a new snapshot bound to that controller index. ("Idea:" framing — the interaction
needs a deliberate decision before wiring.)

## Affected code
- `Editor/Gui/Windows/SnapshotControllerGrid.cs:174` — click handling is inside
  `if (snapshot != null && isHovered && !isDragging)`, so the `snapshot == null` case is unhandled. The
  empty-cell `index` is already computed (`layout.CellToIndex(row, col)`).
- Existing creation API (no new infra needed):
  `Editor/Gui/Interaction/Variations/SnapshotActions.cs:41` —
  `ActivateOrCreateSnapshotAtIndex(int activationIndex)` already does exactly "activate if present, else
  create at this index", and is undo-wrapped downstream.
  Backed by `VariationHandling.CreateOrUpdateSnapshotVariation(activationIndex)` (VariationHandling.cs:333)
  → `SymbolVariationPool.TryCreateVariationForCompositionInstances(...)` (captures current state, command
  for undo).

## Proposed approach
Add an empty-cell branch in the grid: when `snapshot == null && clicked` (and not dragging), call
`SnapshotActions.ActivateOrCreateSnapshotAtIndex(index)` and close the popup. ~10–20 lines, reusing the
existing API; no new command/capture logic.

## Risks / side-effects
- Low technical risk (creation is undoable). The real question is interaction safety: empty-click-creates
  can fire accidentally while navigating the grid.

## Open questions (the reason this is a plan, not an auto-edit)
- Create **and** immediately apply (matches `ActivateOrCreateSnapshotAtIndex`), or just create?
- Capture the current composition state (default of the existing API) or create an empty snapshot?
- Auto-name "untitled" (existing default) or prompt? Existing inline rename in `SnapshotControlView`
  already covers post-hoc naming.
- Accidental-creation guard — require the slot to be clearly empty/hovered, any modifier, or a confirm?
