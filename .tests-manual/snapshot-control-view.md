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
  prev/next arrow buttons, and three action buttons on the right.
- Below it, both enabled operators are listed with their captured parameters,
  ordered by their vertical position in the graph.
- The Variations window shows a new snapshot thumbnail.

## Step: Editing a parameter from the control view

**Action:**
Drag a value of one of the listed parameters in the snapshot control view.

**Expected:**
- The value changes exactly like in the regular parameter view, and the graph
  output updates live.
- After a moment, the write and revert buttons in the selector bar become
  enabled (the current state differs from the snapshot).

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

## Step: No control view for read-only library compositions

**Action:**
Navigate into a Lib-namespace operator (e.g. enter `[Blob]` with `i`) and
deselect everything.

**Expected:**
- The Parameter window does not show the snapshot control view.
