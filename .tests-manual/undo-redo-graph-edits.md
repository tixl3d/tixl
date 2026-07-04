---
id: undo-redo-graph-edits
title: Undo/Redo of Graph & Timeline Edits
added: 2026-06-02
added-in-version: 4.3
scope: graph-window
tags: [user, edge, essential]
prerequisites:
  - A project you can edit is open with at least one operator on the graph.
  - The Graph Window is visible.
related-help:
  - ../.help/using/graph-window.md
---

A sweep of the everyday graph and timeline edits, checking that undo and redo
behave for each one. For every step, `Ctrl+Z` should cleanly take the edit back
and `Ctrl+Y` should put it right again — nothing should be left stranded, undone
to the wrong thing, or do nothing at all.

## Step: Undo/redo adding a section

**Action:**
With the [ui:Graph|Graph Window] focused, press `Shift+S` (or right-click → `Add...` →
`Section`) to create a section. Press `Esc` to leave its rename mode,
then press `Ctrl+Z`, then `Ctrl+Y`.

**Expected:**
- The section appears on creation.
- `Ctrl+Z` removes it.
- `Ctrl+Y` brings it back in the same place.

## Step: Undo/redo deleting a section

**Action:**
Select an existing section and press `Del` to delete it, then press `Ctrl+Z`,
then `Ctrl+Y`.

**Expected:**
- The section frame is removed on delete; operators inside stay where they are.
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
In a graph that contains time clips, open the [ui:Timeline] and drag a clip to a new
position (and/or layer). Press `Ctrl+Z`, then `Ctrl+Y`.

**Expected:**
- `Ctrl+Z` returns the clip to its original time range and layer.
- `Ctrl+Y` moves it back to the dragged position.
- Dragging, undo, and redo all operate on the correct clip — nothing is left
  stranded or duplicated.

## Step: Undo/redo adding an input or output

**Action:**
On an operator you can edit, right-click → `Add...` → `Add input parameter...`
(or `Add output...`), add one, then press `Ctrl+Z`, then `Ctrl+Y`. (Each step
rebuilds the operator, so expect a brief pause.)

**Expected:**
- The new input or output node appears after adding.
- `Ctrl+Z` removes it again (the node disappears).
- `Ctrl+Y` brings it back with the same name and type.

## Step: Undo removing a connected input or output

**Action:**
Take an input or output that has at least one cable attached. Select its node
and press `Del` to remove it, then press `Ctrl+Z`.

**Expected:**
- The node and its cable(s) disappear on delete.
- `Ctrl+Z` restores the node **and** re-attaches its previous cable(s).
- The restored input keeps the value it had before.
- `Ctrl+Y` removes it again.

## Step: Undo/redo reordering inputs

**Action:**
On an operator you can edit that has at least two inputs, open the Parameter
Settings (the parameter list with the gear/settings view) and drag one parameter
to a new spot in the list. Press `Ctrl+Z`, then `Ctrl+Y`. (Each step rebuilds the
operator.)

**Expected:**
- The parameter order changes after the drag.
- `Ctrl+Z` restores the previous order.
- `Ctrl+Y` re-applies the new order.
- Input values and cables stay untouched throughout.

## Step: Combine / Duplicate clear the undo history

**Action:**
Select one or more operators and use `Combine into symbol` (or `Duplicate as new
symbol`) to make a new operator. Read the dialog, then go ahead and finish, and
try `Ctrl+Z`.

**Expected:**
- The dialog warns that this makes a new operator and can't be undone (it clears
  the undo history).
- After finishing, `Ctrl+Z` does **not** undo the combine/duplicate — and it also
  won't undo edits you made earlier, because the history was cleared.

## Step: Undo adding a preset

**Action:**
With an operator selected, open the Presets window and create a preset from the
current parameter values, then press `Ctrl+Z`.

**Expected:**
- The new preset appears in the Presets window on creation.
- `Ctrl+Z` removes that preset again.
