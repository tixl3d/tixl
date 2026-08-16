---
id: snapshot-control-view
title: Snapshot Control View
scope: parameter-window
tags: [user, essential]
added: 2026-06-13
added-in-version: 4.3
prerequisites:
  - A writable user project is open (not a Lib-namespace composition).
  - The Graph, Parameter and Variations windows are visible.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

When nothing is selected in the graph, the [ui:ParameterWindow|Parameter window] shows the [ui:ControlView|snapshot control view] — a single place to capture, name, switch, and tweak your snapshots. This walks through the selector bar at the top, the editable parameter rows for each operator below it, and how everything stays in step with the Variations window.

## Step: Enabling operators for snapshots

**Action:**
In the [ui:Graph|Graph] window:
1. Add two operators with adjustable parameters (e.g. `[Blob]` and `[RadialGradient]`).
2. Right-click each one and choose "Control with Snapshots" from the context menu.
3. Click on the empty graph background so nothing is selected.

**Expected:**
- The Parameter window shows the snapshot control view with a selector bar at the
  top: a zero-padded index button, a snapshot dropdown, prev/next arrows, a write
  button, an add (+) button, and an actions (…) menu.
- Although no snapshot exists yet, both operators are already listed below the bar
  with their controlled parameters in rounded panels, ordered by their vertical
  position in the graph, and the values can be dragged and edited as usual.
- Without a snapshot to compare against, rows highlight like in the regular
  parameter view (bright = non-default value).
- The index button reads "-" and the dropdown "No Snapshots"; the prev/next
  arrows, the write button and the actions (…) menu are disabled, while the
  add (+) button is emphasized.
- Undoing both context-menu toggles (`Ctrl+Z`) leaves no controlled operator: the
  list is then replaced by a "No operators are controlled by snapshots." message.
  Redo (`Ctrl+Y`) brings the two operators back.

## Step: Creating the first snapshot

**Action:**
1. Click the add (+) button at the right end of the selector bar.

**Expected:**
- A text field is focused immediately so the snapshot can be named; typing a
  name and pressing Enter (or clicking away) stores it.
- The new snapshot is the active one; the index button shows its zero-padded
  controller index and the actions (…) menu becomes available (holding Revert /
  Rename / Update thumbnail / Remove).
- The operator list keeps showing the same parameters, now compared against the
  snapshot (all rows muted, since the values match it).
- The [ui:VariationWindow|Variations window] shows a new snapshot thumbnail.

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
  snapshot has unsaved changes (and while no snapshot exists yet) and muted
  (default) when the values match it.
- The new snapshot is inserted **right behind the previously active one** — it
  takes the next free controller index after that one, and since the list is
  ordered by controller index, it appears directly after the active in the picker
  and the controller grid. (Creating the very first snapshot, with none active,
  falls back to the next free index at the end.)

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

## Step: Modified rows are highlighted; the name click reverts to the snapshot

**Action:**
1. In the snapshot control view, change one parameter's value and wait a moment.
2. Hover the edited parameter's name, then click it. Also hover and click the name of a parameter that matches the snapshot but has a non-default value.

**Expected:**
- The edited parameter's name and value turn bright while parameters matching the snapshot stay muted — regardless of whether the values are at default.
- Hovering the edited parameter's name shows a revert icon and a "Click to reset to snapshot" hint; clicking restores the snapshot's stored value for just that parameter (undoable). The row turns muted again, and if no other parameter differs, the selector bar's write/revert buttons disable.
- On a row matching the snapshot, no revert hint appears and clicking the name changes nothing (in particular, it does **not** reset to default like in the regular parameter view, and no undo entry appears).

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
  row's highlight then fades). Undoable.
- **Write to all snapshots** is enabled only when the value differs from at least
  one snapshot; choosing it writes the current value into every snapshot (one
  undo step). Switching snapshots no longer changes this parameter.
- **Reset to Snapshot** restores the active snapshot's stored value (enabled when
  modified) — the menu equivalent of clicking the parameter's name.
- **Reset to Default** returns the parameter to its operator default (undoable).
- **Disable Snapshot control** removes the parameter from snapshot control (knob
  icon disappears, row leaves the view); it is disabled for the composition's own
  "Inputs" group.

## Step: Reverting to the snapshot

**Action:**
1. Click the revert button (circular arrow) in the selector bar.

**Expected:**
- The edited parameter returns to the value stored in the snapshot.
- The Write button disables again (and Revert greys out in the menu).
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
  snapshots, each showing a thumbnail, its index, and title, ordered by their
  controller index (the same order as the controller-index grid).
- Typing filters the list by title; arrow keys move a single highlight (the
  mouse moves it only when the mouse itself moves); Enter or a click applies
  the highlighted/clicked snapshot and closes the popup.

## Step: Reordering snapshots in the picker

**Action:**
With the picker open in list mode and the search field empty, drag a row up or
down by a few rows.

**Expected:**
- A grip appears on the highlighted row; dragging swaps the snapshot's
  **controller index** with its neighbour's, so the list reorders and the
  controller-index grid (and the index shown in the selector bar) update to
  match — the list, the grid and the MIDI pads all share one order.
- Releasing keeps the new order (undoable with `Ctrl+Z`); a plain click without
  dragging still just applies the snapshot.
- The Variations window's 2D canvas is unaffected — its thumbnails keep their
  free positions (used for Alt-drag spatial blending), independent of this order.

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

## Step: Ops are grouped by their section frames

**Action:**
1. In the graph, select one of the snapshot-enabled operators and press `Shift+A` to wrap it in a section frame; double-click the frame header to give it a label and a color.
2. Deselect everything so the snapshot control view shows.

**Expected:**
- The framed operator now appears under a collapsible group header showing the
  section's label in small, muted all-caps, with a chevron on the left.
- The remaining operator appears under a muted "UNGROUPED" header at the bottom.
- Groups are ordered by the frames' position in the graph (top-to-bottom, then
  left-to-right), ops inside a group likewise.

## Step: Nested sections show as a path

**Action:**
1. In the graph, draw a second, larger section frame around the first one (select the frame and an op outside it, `Shift+A`), so the frames nest. Make sure the outer frame also directly contains a snapshot-enabled op.
2. Deselect everything.

**Expected:**
- The inner section's group header shows the nesting path, e.g. "OUTER / INNER";
  the outer section's own ops sit under a plain "OUTER" header before it. There
  is no indentation — all rows use the full width.
- A section frame without snapshot-enabled ops directly inside it gets no header
  of its own; its name only appears as part of its descendants' paths.
- Section frames without any snapshot-enabled operators anywhere inside them do
  not appear in the list at all.

## Step: Collapsing a group

**Action:**
1. Click a group header in the snapshot control view.
2. Open a second Parameter window (if available) and compare.
3. Check the section frame in the graph.

**Expected:**
- The group collapses to just its header (chevron turns right), hiding its ops;
  nested sections keep their own headers and collapse state. Clicking again
  expands it.
- The collapse state is per Parameter window and is not saved with the project.
- The frame in the graph does not collapse or expand with it (the two states are
  independent).

## Step: Centering a section from its group header

**Action:**
1. Click the crosshair (aim) icon at the right end of a section group header.

**Expected:**
- The graph view smoothly centers on that section frame.
- Nothing gets selected — the Parameter window keeps showing the control view.

## Step: Reverting a whole group

**Action:**
1. Modify parameters on two operators inside the same section group (wait a moment so their rows highlight).
2. Click the revert icon on that group's header.

**Expected:**
- The revert icon on the header is only enabled while one of the group's own ops
  differs from the snapshot (nested sections have their own headers and revert
  buttons).
- Clicking it restores the snapshot values for the ops in that group only — as a
  single undo step; ops outside the group keep their modifications.

## Step: Jumping to an op hidden in a collapsed frame

**Action:**
1. Collapse the section frame **in the graph** (its ops become hidden).
2. In the snapshot control view, click that operator's name.

**Expected:**
- The hidden op still shows its editable parameters in the control view.
- Clicking its name centers the graph on the collapsed frame's header instead of
  selecting the invisible op.

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

## Step: Old projects still behave the same

**Action:**
1. Open a project saved before per-parameter control existed, with operators enabled for snapshots.
2. Look at them in the snapshot control view.
3. *Without changing anything*, watch the title bar / save indicator for an "unsaved changes" marker.

**Expected:**
- Every adjustable parameter of the enabled operators is listed and marked as controlled — exactly like before.
- Just opening and viewing the project does not mark it as changed (no "unsaved changes" appears and nothing lands on the undo history), so the old project is left untouched.

## Step: No control view for read-only library operators

**Action:**
1. Step inside one of TiXL's built-in library operators (e.g. enter `[Blob]` with `i`).
2. Deselect everything.

**Expected:**
- The Parameter window does not show the snapshot control view (these built-in operators are read-only, so there's nothing to capture here).
