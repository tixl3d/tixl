---
id: renaming-outputs
title: Renaming Outputs
added: 2026-06-02
added-in-version: 4.3
scope: graph-window
tags: [essential, edge]
prerequisites:
  - A writable (non-library) operator with at least one output is open as the composition.
  - The Graph Window is visible.
related-help:
  - ../.help/using/graph-window.md
---

Renames an output slot on an editable operator. Like renaming an input, this
rewrites and recompiles the operator's source. The slot keeps its identity, so
existing connections survive, and the rename can be undone (each undo/redo
re-runs the recompile, so it is noticeably slower than a normal undo).

## Step: Selecting an output

**Action:**
In the Graph Window, click the output node of the composition operator (the
slot drawn at the right edge of the composition) so it is the only thing
selected.

**Expected:**
- The output node is highlighted as selected.
- No operator child is selected.

## Step: Opening the rename dialog

**Action:**
Right-click the selected output node to open the context menu, then choose
`Rename output`.

**Expected:**
- The `Rename output` item is present in the context menu (it only appears when
  exactly one output is selected on an editable operator).
- A `Rename output` dialog opens.
- Its text field is focused and pre-filled with the current output name.
- A hint warns that this modifies the operator definition.

## Step: Renaming the output

**Action:**
Clear the field, type a new valid name (e.g. `Result2`), and click
`Rename output`.

**Expected:**
- The dialog closes.
- The operator recompiles and the output is now labelled with the new name in
  the graph and wherever the output appears.

## Step: Undoing the rename

**Action:**
After a successful rename, press `Ctrl+Z`.

**Expected:**
- The operator recompiles and the output reverts to its previous name.
- Any connections to the output are still intact.
- Pressing `Ctrl+Y` (redo) re-applies the new name.

## Step: Rejecting an invalid name

**Action:**
Open the `Rename output` dialog again and type an invalid name — for example a
name that starts with a digit, or the name of an existing input/output.

**Expected:**
- The `Rename output` button is disabled while the name is invalid.
- No rename happens until a valid, unused name is entered.
