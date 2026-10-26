---
id: graph-arrow-navigation
title: Graph Arrow Key Navigation
scope: graph-window
tags: [user]
added: 2026-10-25
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A graph with a vertical stack of at least three snapped operators and a few loose operators beside it.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks moving the selection with the arrow keys in the graph. Arrow keys pick the
neighbouring operator in that direction, keep it in view, and show subtle marks on
the operators each arrow key would select next.

## Step: Walking a snapped stack

**Action:**
Click the top operator of the snapped stack, then press `Down` repeatedly.

**Expected:**
- Each press selects the next operator in the stack, one at a time.
- The Parameter Window follows the selection.

## Step: Jump-target marks appear with keyboard navigation

**Action:**
After the previous step, look at the operators around the selected one.

**Expected:**
- Operators that an arrow key would select show a short, faint line on the side facing the selected operator.
- After each key press the marks grow in quickly, the pressed direction first and the way back last.

## Step: Marks disappear when selecting with the mouse

**Action:**
Click any operator, then drag a selection fence over a few operators.

**Expected:**
- No jump-target marks are shown after the click or the fence selection.
- Pressing an arrow key shows them again.

## Step: Going back the way you came

**Action:**
From a loose operator, press `Right` until the selection lands on a different operator, then press `Left`.

**Expected:**
- `Left` returns to the operator you started from, even if another operator lies closer to the left.
- Repeating opposite keys walks back along the whole path.

## Step: Keeping the selection in view

**Action:**
Zoom in so only a few operators are visible, then keep pressing an arrow key toward the edge of the view.

**Expected:**
- The graph scrolls so the selected operator stays visible with some space to the window edge.
- The zoom level does not change.

## Step: Starting with nothing selected

**Action:**
Click the graph background to clear the selection, then press any arrow key.

**Expected:**
- The operator closest to the centre of the view is selected.

## Step: Arrow keys don't interfere with other states

**Action:**
Press `Tab` to open the symbol browser and use `Up` / `Down` in its list. Close it with `Esc`, then double-click an operator name to rename it and use `Left` / `Right` in the text field.

**Expected:**
- The symbol browser list and the rename field use the arrow keys as before.
- The graph selection does not change while they are open.
