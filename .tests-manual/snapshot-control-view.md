---
id: snapshot-control-view
title: Snapshot Control View
scope: parameter-window
tags: [essential]
added: 2026-06-13
added-in-version: 4.3
prerequisites:
  - A writable user project is open (not a Lib-namespace composition).
  - The Graph, Parameter and Variations windows are visible.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

Covers the snapshot control view shown in the Parameter window when nothing is
selected in the graph: the snapshot selector bar, editable per-operator
parameter rows, and how it stays in sync with the Variations window.

## Step: Enabling operators for snapshots

**Action:**
In the Graph window, add two value-driven operators (e.g. `[Blob]` and
`[RadialGradient]`). Right-click each one and choose "Enable for snapshots"
from the context menu. Then click on the empty graph background so nothing is
selected.

**Expected:**
- The Parameter window shows the snapshot control view with a message that no
  snapshots exist yet and a "Create snapshot" button.

## Step: Creating the first snapshot

**Action:**
Click the "Create snapshot" button in the Parameter window.

**Expected:**
- A selector bar appears at the top: index indicator, a snapshot dropdown,
  prev/next arrow buttons, and write/revert/remove plus an add (+) button.
- A text field is focused immediately so the snapshot can be named; typing a
  name and pressing Enter (or clicking away) stores it.
- The new snapshot is the active one.
- Below the bar, both enabled operators are listed with their captured
  parameters in rounded panels, ordered by their vertical position in the
  graph.
- The Variations window shows a new snapshot thumbnail.

## Step: Renaming a snapshot from the selector bar

**Action:**
Click the index number on the left of the selector bar.

**Expected:**
- The dropdown is replaced by a focused text field pre-filled with the current
  name; editing and pressing Enter renames the snapshot.

## Step: Adding a snapshot with the + button

**Action:**
Change a parameter so the snapshot is modified, then click the + button at the
right end of the selector bar.

**Expected:**
- A new snapshot is created from the current values, becomes active, and its
  name field is focused for immediate renaming.
- The + button is disabled (dimmed) when the current values still match the
  active snapshot.

## Step: A snapshot is shown without manual selection

**Action:**
Navigate into another composition and back (or restart TiXL), then click the
graph background so the snapshot control view appears again.

**Expected:**
- A snapshot is already displayed — the one matching the current parameter
  values if one exists, otherwise the first.
- Merely opening the view does not change any parameter values (no new undo
  entry appears).

## Step: Editing a parameter from the control view

**Action:**
Drag a value of one of the listed parameters in the snapshot control view.

**Expected:**
- The value changes exactly like in the regular parameter view, and the graph
  output updates live.
- After a moment, the write and revert buttons in the selector bar become
  enabled (the current state differs from the snapshot).

## Step: Modified rows are highlighted with a revert button

**Action:**
In the snapshot control view, change one parameter's value and wait a moment.

**Expected:**
- The edited parameter's name and value turn bright while parameters matching
  the snapshot stay muted — regardless of whether the values are at default.
- A revert icon appears at the right end of the edited row, in the gap kept
  free beside the value.
- Clicking it restores the snapshot's stored value for just that parameter
  (undoable); the row turns muted again, and if no other parameter differs,
  the selector bar's write/revert buttons disable.

## Step: Scaling a change with the revert handle

**Action:**
Modify a parameter, then press and drag horizontally on its revert icon.

**Expected:**
- An infinity-slider overlay appears showing a factor starting at 1.
- Dragging the factor towards 0 moves the value back to the snapshot; dragging
  above 1 amplifies the difference beyond the current value.
- Releasing keeps the scaled value (undoable as one step); releasing at factor
  1 leaves the value unchanged.

## Step: Reverting to the snapshot

**Action:**
Click the revert button (circular arrow) in the selector bar.

**Expected:**
- The edited parameter returns to the value stored in the snapshot.
- The write and revert buttons become disabled again.
- `Ctrl+Z` restores the edited value; `Ctrl+Y`/redo re-applies the snapshot.

## Step: Writing changes into the snapshot

**Action:**
Change a parameter again, then click the write button (camera icon) in the
selector bar.

**Expected:**
- The write and revert buttons become disabled — the snapshot now matches the
  current values.
- The snapshot keeps its index and position in the Variations window.
- `Ctrl+Z` restores the previous snapshot content (write/revert become enabled
  again).

## Step: Switching snapshots with the arrows

**Action:**
Create a second snapshot with the + button in the Variations window after
changing some values. Then use the prev/next arrow buttons in the Parameter
window's selector bar.

**Expected:**
- The arrows cycle through the snapshots in index order and apply each one.
- The index indicator and dropdown label follow the active snapshot.
- The active thumbnail highlight in the Variations window follows along.

## Step: Selecting a snapshot from the dropdown

**Action:**
Open the snapshot dropdown in the selector bar and pick the other snapshot.

**Expected:**
- The chosen snapshot is applied; parameter rows update to its values.

## Step: Hover highlight between view and graph

**Action:**
Hover the mouse over one of the operator panels in the snapshot control view,
then over the same operator's node in the Graph window.

**Expected:**
- Hovering the panel highlights the matching operator node in the graph.
- Hovering the operator node in the graph brightens its panel in the control
  view.

## Step: Header shown when entering a composition

**Action:**
Double-click an operator in the graph to enter (open) it, without selecting
anything inside.

**Expected:**
- The Parameter window immediately shows the composition's name and namespace
  header above the snapshot control view — no background click required.

## Step: Jumping to an operator from the list

**Action:**
Click on an operator's name in the snapshot control view's list.

**Expected:**
- The operator is selected and centered in the Graph window.
- The Parameter window switches to the regular parameter view for that
  operator. Clicking the graph background returns to the snapshot control
  view.

## Step: Stale entries after deleting an operator

**Action:**
With a snapshot active that contains values for both operators, delete one of
the enabled operators in the graph (`Del`). Deselect everything.

**Expected:**
- The deleted operator no longer appears as an editable group; instead a muted
  row marks its leftover values, with a small trash button to remove them from
  the snapshot.
- Undoing the deletion (`Ctrl+Z`) restores the editable group.

## Step: Removing a snapshot

**Action:**
Click the trash button on the right of the selector bar.

**Expected:**
- The snapshot disappears from the dropdown and from the Variations window.
- `Ctrl+Z` brings it back in both places.

## Step: Enabling a single parameter for control

**Action:**
Add a new operator (e.g. `[Transform]`) that is *not* enabled for snapshots.
Select it, right-click one of its value parameters (e.g. `Scale`) in the
Parameter window and choose "Control with Snapshots".

**Expected:**
- A green knob icon appears in the connection area left of the parameter's
  name.
- The operator now appears in the snapshot control view with only that one
  parameter listed.
- If the parameter had a non-default value, existing snapshots now include it
  (applying another snapshot resets it).
- `Ctrl+Z` reverts both the toggle and the snapshot updates.

## Step: Disabling a single parameter of a fully enabled op

**Action:**
For an operator enabled via the graph context menu (all parameters
controlled), right-click one of its parameters and uncheck "Control with
Snapshots".

**Expected:**
- The knob icon disappears, and the parameter vanishes from the snapshot
  control view while the op's other parameters stay.
- The parameter's stored values are removed from all snapshots — switching
  snapshots no longer changes it.
- Editing this parameter no longer makes write/revert light up in the
  selector bar.

## Step: Disabling the last controlled parameter

**Action:**
Uncheck "Control with Snapshots" on every remaining controlled parameter of that
operator.

**Expected:**
- After the last one, the operator is no longer enabled for snapshots at all:
  it disappears from the snapshot control view, and the graph context menu's
  "Enable for snapshots" is unchecked.

## Step: Bulk toggle resets the per-parameter selection

**Action:**
With an operator that has only some parameters enabled, right-click it in the
graph and choose "Enable for snapshots".

**Expected:**
- All of its parameters become controlled (knob icons everywhere, all rows
  listed in the control view).

## Step: Old project files keep per-op semantics

**Action:**
Open a project saved before per-parameter control existed, with operators
enabled for snapshots. Inspect them in the snapshot control view, then close
TiXL *without changing anything* and check the project's `.t3ui` file
modification time.

**Expected:**
- All blendable parameters of the enabled operators are listed and marked as
  controlled — identical to the old behavior.
- The `.t3ui` files are not rewritten by merely loading and viewing.

## Step: No control view for read-only library compositions

**Action:**
Navigate into a Lib-namespace operator (e.g. enter `[Blob]` with `i`) and
deselect everything.

**Expected:**
- The Parameter window does not show the snapshot control view.
