---
id: renaming-outputs
title: Renaming Outputs
added: 2026-06-02
added-in-version: 4.3
scope: graph-window
tags: [user, essential, edge]
prerequisites:
  - A writable (non-library) operator with at least one output is open as the composition.
  - The Graph Window is visible.
related-help:
  - ../.help/using/graph-window.md
---

Gives an output of one of your own operators a new name. TiXL rebuilds the
operator behind the scenes, so expect a short pause — but the output keeps its
identity, which means anything wired to it stays connected. The rename can be
undone, though each undo or redo triggers the same rebuild, so it feels a little
slower than an ordinary undo.

## Step: Selecting an output

**Action:**
In the [ui:Graph|Graph Window], click the output node of the operator you're editing (the
slot drawn at the right edge) so it is the only thing selected.

**Expected:**
- The output node is highlighted as selected.
- No operator inside the graph is selected — just the output.

## Step: Opening the rename dialog

**Action:**
Right-click the selected output node to open the context menu, then choose
`Rename output`.

**Expected:**
- The `Rename output` item is present in the context menu (it only shows up when
  exactly one output is selected on an operator you can edit).
- A `Rename output` dialog opens.
- Its text field is focused and already filled in with the current output name.
- A hint warns that this changes the operator itself.

## Step: Renaming the output

**Action:**
Clear the field, type a new valid name (e.g. `Result2`), and click
`Rename output`.

**Expected:**
- The dialog closes.
- After a short rebuild, the output shows the new name — on the node in the graph
  and everywhere else the output appears.

## Step: Undoing the rename

**Action:**
After a successful rename, press `Ctrl+Z`.

**Expected:**
- After a short rebuild, the output goes back to its previous name.
- Anything wired to the output is still connected.
- Pressing `Ctrl+Y` (redo) brings the new name back.

## Step: Rejecting an invalid name

**Action:**
Open the `Rename output` dialog again and type a name that won't work — for
example one that starts with a digit, or a name already used by another input or
output.

**Expected:**
- The `Rename output` button is greyed out while the name isn't allowed.
- Nothing is renamed until you enter a valid, unused name.
