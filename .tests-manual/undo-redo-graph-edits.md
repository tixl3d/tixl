---
id: undo-redo-graph-edits
title: Undo/Redo of Graph & Timeline Edits
added: 2026-06-02
added-in-version: 4.3
scope: graph-window
tags: [edge, essential]
prerequisites:
  - A writable project is open with at least one operator on the graph.
  - The Graph Window is visible.
related-help:
  - ../.help/using/graph-window.md
---

Regression net for the commands behind common graph and timeline edits. These
commands were reworked to re-resolve their target by id each time they run (so
they survive operator hot-reloads on the undo stack) instead of holding a live
reference. Each step verifies a clean undo/redo round-trip — if undo no-ops,
restores the wrong thing, or throws, that's the regression this set guards.

## Step: Undo/redo adding an annotation

**Action:**
With the Graph Window focused, press `Shift+A` (or right-click → `Add...` →
`Add Annotation`) to create an annotation. Press `Esc` to leave its rename mode,
then press `Ctrl+Z`, then `Ctrl+Y`.

**Expected:**
- The annotation appears on creation.
- `Ctrl+Z` removes it.
- `Ctrl+Y` brings it back in the same place.

## Step: Undo/redo deleting an annotation

**Action:**
Select an existing annotation and press `Del` to delete it, then press `Ctrl+Z`,
then `Ctrl+Y`.

**Expected:**
- The annotation is removed on delete.
- `Ctrl+Z` restores it with its original text and position.
- `Ctrl+Y` deletes it again.

## Step: Undo/redo changing a parameter value

**Action:**
Select an operator, change one of its numeric parameters in the Parameter
Window (drag or type a new value), then press `Ctrl+Z`, then `Ctrl+Y`.

**Expected:**
- `Ctrl+Z` restores the original value.
- `Ctrl+Y` re-applies the new value.
- The output updates to match after each.

## Step: Undo/redo moving a time clip

**Action:**
In a composition that contains time clips, open the Timeline and drag a clip to
a new position (and/or layer). Press `Ctrl+Z`, then `Ctrl+Y`.

**Expected:**
- `Ctrl+Z` returns the clip to its original time range and layer.
- `Ctrl+Y` moves it back to the dragged position.
- Dragging, undo, and redo all operate on the correct clip — nothing is left
  stranded or duplicated.

## Step: Undo/redo adding an input or output

**Action:**
On an editable (non-library) composition, right-click → `Add...` →
`Add input parameter...` (or `Add output...`), add a slot, then press `Ctrl+Z`,
then `Ctrl+Y`. (Each step recompiles, so expect a brief pause.)

**Expected:**
- The new input/output node appears after adding.
- `Ctrl+Z` removes the slot again (the node disappears).
- `Ctrl+Y` re-adds it with the same name and type.

## Step: Undo removing a connected input or output

**Action:**
Take an input or output that has at least one connection. Select its node and
press `Del` to remove it, then press `Ctrl+Z`.

**Expected:**
- The slot and its connection(s) disappear on delete.
- `Ctrl+Z` restores the slot **and** reconnects its previous connection(s).
- The restored input keeps its previous default value.
- `Ctrl+Y` removes it again.

## Step: Undo/redo reordering inputs

**Action:**
On an editable operator with at least two inputs, open the Parameter Settings
(the parameter list with the gear/settings view) and drag one parameter to a new
position in the order. Press `Ctrl+Z`, then `Ctrl+Y`. (Each step recompiles.)

**Expected:**
- The parameter order changes after the drag.
- `Ctrl+Z` restores the previous order.
- `Ctrl+Y` re-applies the new order.
- Input values and connections are unaffected throughout.

## Step: Combine / Duplicate clear the undo history

**Action:**
Select one or more operators and use `Combine into symbol` (or `Duplicate as new
symbol`) to create a new operator. Read the dialog, then complete the operation
and try `Ctrl+Z`.

**Expected:**
- The dialog shows a hint that the operation creates a new operator and can't be
  undone (it clears the undo history).
- After completing, `Ctrl+Z` does **not** revert the combine/duplicate, and does
  not revert edits made before it (the history was cleared).

## Step: Undo adding a preset

**Action:**
With an operator selected, open the Presets window and create a preset from the
current parameter values, then press `Ctrl+Z`.

**Expected:**
- The new preset appears in the Presets window on creation.
- `Ctrl+Z` removes that preset again.
