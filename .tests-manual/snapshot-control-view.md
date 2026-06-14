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

Covers the snapshot control view shown in the Parameter window when nothing is selected in the graph: the snapshot selector bar, editable per-operator parameter rows, and how it stays in sync with the Variations window.

## Step: Enabling operators for snapshots

**Action:**
In the Graph window:
1. Add two value-driven operators (e.g. `[Blob]` and `[RadialGradient]`).
2. Right-click each one and choose "Enable for snapshots" from the context menu.
3. Click on the empty graph background so nothing is selected.

**Expected:**
- The Parameter window shows the snapshot control view with a message that no snapshots exist yet and a "Create snapshot" button.

## Step: Creating the first snapshot

**Action:**
1. Click the "Create snapshot" button in the Parameter window.

**Expected:**
- A selector bar appears at the top: a zero-padded index button, a snapshot
  dropdown, prev/next arrows, a revert button, an add (+) button, and an actions
  (…) menu (holding Write changes / Rename / Update thumbnail / Remove).
- A text field is focused immediately so the snapshot can be named; typing a
  name and pressing Enter (or clicking away) stores it.
- The new snapshot is the active one.
- Below the bar, both enabled operators are listed with their captured parameters in rounded panels, ordered by their vertical position in the graph.
- The Variations window shows a new snapshot thumbnail.

## Step: Controller-index grid

**Action:**
1. Click the index number on the left of the selector bar.

**Expected:**
- An 8×8 grid opens in reading order (index 1 at the top-left); cells holding a
  snapshot are green, the active (live) one magenta, empty cells are dim.
- The header has, right-aligned, a **settings gear** and then a documentation
  button (hovering the latter explains the controller index).
- The gear opens a menu with **Show thumbnails** and — when a supported controller
  defines a layout — a **Controller layout** list (checkmark on the active one;
  e.g. the APC Mini's bottom-up, 0-based pad grid that matches its hardware).
- With **Show thumbnails** on, cells show the snapshot's thumbnail and the
  green/magenta state moves to the cell **border**; the index stays legible over
  a dark backing.
- Hovering a filled cell shows a move cursor (it can be dragged to another slot).
- Clicking a used cell applies that snapshot and closes the grid; hovering one
  shows its title (and previews it live when hover preview is enabled).
- While the grid is open, hovering it does not highlight the operator panels in
  the control view behind it.

## Step: Reassigning a controller index by drag

**Action:**
With at least two snapshots, open the controller-index grid and drag a used cell
onto an empty cell, then (in a second drag) onto another used cell.

**Expected:**
- While dragging, the source cell dims evenly (whether it is the active snapshot
  or not) and a small chip showing its index and name follows the cursor; the
  cell under the cursor is outlined.
- Dropping on an empty cell moves the snapshot to that index; dropping on a used
  cell swaps the two snapshots' indices.
- The index shown in the selector bar (and the order the prev/next arrows cycle)
  follows the change; releasing back on the source cell, or outside the grid,
  leaves everything unchanged and does not apply the snapshot.
- `Ctrl+Z` restores the previous index assignment (the swap reverts both).

## Step: Renaming a snapshot

**Action:**
Open the actions (…) menu in the selector bar and choose "Rename". Also try
**double-clicking the snapshot name** (the picker dropdown) directly.

**Expected:**
- Either route replaces the dropdown with a focused text field pre-filled with
  the current name; editing and pressing Enter renames the snapshot.
- The double-click does not leave the picker popup open.

## Step: Adding a snapshot with the + button

**Action:**
1. Change a parameter so the snapshot is modified. 
2. Click the + button at the right end of the selector bar.

**Expected:**
- A new snapshot is created from the current values, becomes active, and its
  name field is focused for immediate renaming.
- The + button is always enabled; it is emphasized (brighter) when the active
  snapshot has unsaved changes and muted (default) when the values match it.
- The new snapshot is inserted **right behind the previously active one** — it
  takes the next free controller index above that one, and on the canvas it lands
  in the slot just after the active, shifting the snapshots that followed one slot
  later (no overlap or gap), so it sorts directly after in the picker too.
  (Creating the very first snapshot, with none active, falls back to the next free
  slot at the end.)

## Step: A snapshot is shown without manual selection

**Action:**
1. Navigate into another composition and back (or restart TiXL).
2. Click the graph background so the snapshot control view appears again.

**Expected:**
- A snapshot is already displayed — the one matching the current parameter values if one exists, otherwise the first.
- Merely opening the view does not change any parameter values (no new undo entry appears).

## Step: Editing a parameter from the control view

**Action:**
1. Drag a value of one of the listed parameters in the snapshot control view.

**Expected:**
- The value changes exactly like in the regular parameter view, and the graph output updates live.
- After a moment, the write and revert buttons in the selector bar become enabled (the current state differs from the snapshot).

## Step: Modified rows are highlighted with a revert button

**Action:**
1. In the snapshot control view, change one parameter's value and wait a moment.

**Expected:**
- The edited parameter's name and value turn bright while parameters matching the snapshot stay muted — regardless of whether the values are at default.
- A revert icon appears at the right end of the edited row, in the gap kept free beside the value.
- Clicking it restores the snapshot's stored value for just that parameter (undoable); the row turns muted again, and if no other parameter differs, the selector bar's write/revert buttons disable.

## Step: Scaling a change with the revert handle

**Action:**
1. Modify a parameter.
2. Then press and drag horizontally on its revert icon.

**Expected:**
- An infinity-slider overlay appears showing a factor starting at 1.
- Dragging the factor towards 0 moves the value back to the snapshot; dragging above 1 amplifies the difference beyond the current value.
- Releasing keeps the scaled value (undoable as one step); releasing at factor 1 leaves the value unchanged.

## Step: Per-parameter actions menu

**Action:**
Each parameter row has a small actions button (gear) at its right edge. Click it
to open the menu, on both an unchanged parameter and one you've edited. Then
**right-click the parameter** and compare.

**Expected:**
- The gear menu lists: Write to snapshot, Write to all snapshots, Reset to
  Snapshot, Reset to Default, then below a divider, Disable Snapshot control.
- The parameter **right-click menu** shows the same Write / Reset to Snapshot
  actions under a **"Snapshot control"** section header (alongside "Control with
  Snapshots"); its "Reset to Default" stays in the Parameter section.
- **Write to snapshot** is enabled only when the parameter differs from the
  active snapshot; choosing it writes the current value into the snapshot (the
  row's revert button then disables). Undoable.
- **Write to all snapshots** is enabled only when the value differs from at least
  one snapshot; choosing it writes the current value into every snapshot (one
  undo step). Switching snapshots no longer changes this parameter.
- **Reset to Snapshot** restores the active snapshot's stored value (enabled when
  modified) — the menu equivalent of the row's revert icon.
- **Reset to Default** returns the parameter to its operator default (undoable).
- **Disable Snapshot control** removes the parameter from snapshot control (knob
  icon disappears, row leaves the view); it is disabled for the composition's own
  "Inputs" group.

## Step: Reverting to the snapshot

**Action:**
1. Click the revert button (circular arrow) in the selector bar.

**Expected:**
- The edited parameter returns to the value stored in the snapshot.
- The revert button disables again (and "Write changes" greys out).
- `Ctrl+Z` restores the edited value; `Ctrl+Y`/redo re-applies the snapshot.

## Step: Writing changes into the snapshot

**Action:**
1. Change a parameter again.
2. Click the write button (camera icon) in the selector bar.

**Expected:**
- The write and revert buttons become disabled — the snapshot now matches the current values.
- The snapshot keeps its index and position in the Variations window.
- `Ctrl+Z` restores the previous snapshot content (write/revert become enabled again).

## Step: Switching snapshots with the arrows

**Action:**
1. Create a second snapshot with the + button in the Variations window after changing some values
2. Use the prev/next arrow buttons in the Parameter window's selector bar.

**Expected:**
- The arrows cycle through the snapshots in index order and apply each one.
- The index indicator and dropdown label follow the active snapshot.
- The active thumbnail highlight in the Variations window follows along.

## Step: Cycling and renaming snapshots with the keyboard

**Action:**
Click inside the snapshot control view to focus it, then press the Left / Right
arrow keys, and press Enter. Then click into a parameter value (or the picker
search) and press the arrows / Enter again. Also hover the prev/next bar arrows.

**Expected:**
- While the window is focused a thin focus frame is drawn around it (as on the
  Graph / Output windows).
- Left / Right cycle to the previous / next snapshot (same as the bar arrows);
  the arrow buttons' tooltips name the Left / Right arrow shortcut.
- Enter starts renaming the active snapshot.
- Neither the arrows nor Enter fire while a value is being edited or a text field
  (rename / search) is active.

## Step: Selecting a snapshot from the picker

**Action:**
1. Open the snapshot dropdown in the selector bar and pick the other snapshot.

**Expected:**
- The picker opens as a popup with a focused search field and a list of
  snapshots, each showing a thumbnail, its index, and title, ordered to match
  the Variations window grid (top-left first).
- Typing filters the list by title; arrow keys move a single highlight (the
  mouse moves it only when the mouse itself moves); Enter or a click applies
  the highlighted/clicked snapshot and closes the popup.

## Step: Reordering snapshots in the picker

**Action:**
With the picker open in list mode and the search field empty, drag a row up or
down by a few rows.

**Expected:**
- A grip appears on the highlighted row; dragging swaps the snapshot past its
  neighbours, and the order also updates in the Variations window grid (both
  follow canvas position).
- Releasing keeps the new order (undoable with `Ctrl+Z`); a plain click without
  dragging still just applies the snapshot.

## Step: Picker hover preview and canvas mode

**Action:**
With the picker open, toggle the hover-preview icon (top-right) on and move the
highlight over different snapshots. Then toggle the canvas icon.

**Expected:**
- With hover preview on, the output previews the highlighted snapshot; closing
  the popup without picking restores the previous state.
- The canvas toggle switches the popup to the embedded snapshot canvas; the
  list toggle returns to the list.

## Step: Hover highlight between view and graph

**Action:**
1. Hover the mouse over one of the operator panels in the snapshot control view.
2. Then over the same operator's node in the Graph window.

**Expected:**
- Hovering the panel highlights the matching operator node in the graph.
- Hovering the operator node in the graph brightens its panel in the control view.

## Step: Header shown when entering a composition

**Action:**
1. Double-click an operator in the graph to enter (open) it, without selecting anything inside.

**Expected:**
- The Parameter window immediately shows the composition's name and namespace
  header above the snapshot control view — no background click required.

## Step: Jumping to an operator from the list

**Action:**
1. Click on an operator's name in the snapshot control view's list.

**Expected:**
- The operator is selected and centered in the Graph window.
- The Parameter window switches to the regular parameter view for that operator. Clicking the graph background returns to the snapshot control view.

## Step: Stale entries after deleting an operator

**Action:**
With a snapshot active that contains values for both operators
1. Delete one of the enabled operators in the graph (`Del`).
2. Deselect everything.

**Expected:**
- The deleted operator no longer appears as an editable group; instead a muted row marks its leftover values, with a small trash button to remove them from the snapshot.
- Undoing the deletion (`Ctrl+Z`) restores the editable group.

## Step: Removing a snapshot

**Action:**
1. Click the trash button on the right of the selector bar.

**Expected:**
- The snapshot disappears from the dropdown and from the Variations window.
- `Ctrl+Z` brings it back in both places.

## Step: Enabling a single parameter for control

**Action:**
1. Add a new operator (e.g. `[Transform]`) that is *not* enabled for snapshots.
2. Select it
3. Right-click one of its value parameters (e.g. `Scale`) in the Parameter window and choose "Control with Snapshots".

**Expected:**
- A green knob icon appears in the connection area left of the parameter's name.
- The operator now appears in the snapshot control view with only that one parameter listed.
- If the parameter had a non-default value, existing snapshots now include it (applying another snapshot resets it).
- `Ctrl+Z` reverts both the toggle and the snapshot updates.

## Step: Disabling a single parameter of a fully enabled op

**Action:**
For an operator enabled via the graph context menu (all parameters controlled)
1. Right-click one of its parameters and uncheck "Control with Snapshots".

**Expected:**
- The knob icon disappears, and the parameter vanishes from the snapshot control view while the op's other parameters stay.
- The parameter's stored values are removed from all snapshots — switching snapshots no longer changes it.
- Editing this parameter no longer makes write/revert light up in the selector bar.

## Step: Disabling the last controlled parameter

**Action:**
1. Uncheck "Control with Snapshots" on every remaining controlled parameter of that operator.

**Expected:**
- After the last one, the operator is no longer enabled for snapshots at all:
  it disappears from the snapshot control view, and the graph context menu's "Enable for snapshots" is unchecked.

## Step: Bulk toggle resets the per-parameter selection

**Action:**
With an operator that has only some parameters enabled:
1. Right-click on it in the graph and choose "Enable for snapshots".

**Expected:**
- All of its parameters become controlled (knob icons everywhere, all rows listed in the control view).

## Step: Old project files keep per-op semantics

**Action:**
1. Open a project saved before per-parameter control existed, with operators enabled for snapshots.
2. Inspect them in the snapshot control view.
3. Close TiXL *without changing anything* and check the project's `.t3ui` file modification time.

**Expected:**
- All blendable parameters of the enabled operators are listed and marked as controlled — identical to the old behavior.
- The `.t3ui` files are not rewritten by merely loading and viewing.

## Step: No control view for read-only library compositions

**Action:**
1. Navigate into a Lib-namespace operator (e.g. enter `[Blob]` with `i`).
2. Deselect everything.

**Expected:**
- The Parameter window does not show the snapshot control view.
