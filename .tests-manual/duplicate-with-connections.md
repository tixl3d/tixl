---
id: duplicate-with-connections
title: Duplicate With Connections
added: 2026-06-12
added-in-version: 4.3
scope: graph-window
tags: [essential]
prerequisites:
  - A writable project is open in the Graph Window.
  - A composition you can edit with a few operators wired together.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Covers `Duplicate With Connections` (`Ctrl+Shift+D`), which duplicates the
selected operators — like plain `Duplicate` (`Ctrl+D`), pasting at the mouse
position — but also keeps the wiring that reaches into the selection from
outside. Connections feeding the selection are recreated on the duplicates, and
where a selected operator feeds an external **multi-input**, the duplicate's
output is appended alongside the original. Single external inputs are
deliberately left alone so the duplicate doesn't steal the original's connection.

## Step: Duplicate an operator that is fed from outside the selection

**Action:**
Wire a source operator into the input of a second operator. Select only the
second operator and press `Ctrl+Shift+D`.

**Expected:**
- A copy of the second operator appears at the mouse position.
- The copy is connected to the **same** source operator on the same input — the
  original's connection is untouched.
- The new copy is now the selected operator.

## Step: Compare against plain Duplicate

**Action:**
Undo (`Ctrl+Z`) until back to the single wired pair. Select the second operator
and press `Ctrl+D` (plain duplicate).

**Expected:**
- A copy appears, but it has **no** connection to the source operator (plain
  duplicate keeps only connections internal to the selection).
- This confirms the difference: `Ctrl+Shift+D` preserves the inbound wiring,
  `Ctrl+D` does not.

## Step: Duplicate two connected operators together

**Action:**
Select two operators that are wired to each other and also receive an input from
a third, unselected operator. Press `Ctrl+Shift+D`.

**Expected:**
- Both operators are duplicated and remain wired to each other.
- The inbound connection from the third operator is recreated on the matching
  duplicate.

## Step: Duplicate an operator feeding an external multi-input

**Action:**
Wire a selected operator into a multi-input target (e.g. an operator that
collects several inputs of the same type). Select only that upstream operator
and press `Ctrl+Shift+D`.

**Expected:**
- The duplicate's output is appended to the same multi-input, directly after the
  original's connection — both the original and the duplicate now feed the
  target.
- A single (non-multi) external input would **not** receive the duplicate's
  output; only multi-inputs are appended to.

## Step: Snapped siblings are pushed down by the new input line

**Action:**
Build a snapped vertical stack where a multi-input operator has further
operators snapped directly below its input lines. Select an operator feeding
that multi-input and press `Ctrl+Shift+D`.

**Expected:**
- The target grows by one input line for the appended connection, and the
  operators snapped below that line move down by one row — the same behavior as
  interactively dropping a connection onto a connection line.
- The stack stays snapped; nothing overlaps.
- `Ctrl+Z` removes the duplicate and moves the pushed operators back up in the
  same single undo step.

## Step: Undo restores the original graph

**Action:**
After any of the duplications above, press `Ctrl+Z` once.

**Expected:**
- The duplicated operators and every connection added for them are removed in a
  single step.
- The graph matches its state before the duplication.

## Step: Duplicate With Connections from the context menu

**Action:**
Right-click a selected operator that is fed from outside the selection and choose
**Duplicate With Connections** from the context menu.

**Expected:**
- The same result as the `Ctrl+Shift+D` shortcut: the duplicate appears with its
  inbound connection preserved.
