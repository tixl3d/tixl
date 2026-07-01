---
id: duplicate-with-connections
title: Duplicate With Connections
added: 2026-06-12
added-in-version: 4.3
scope: graph-window
tags: [user, essential]
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A graph you can edit, with a few operators wired together.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks `Duplicate With Connections` (`Ctrl+Shift+D`). Like plain `Duplicate`
(`Ctrl+D`), it copies the selected operators and drops them at the mouse — but it
also keeps the cables that fed into your selection from operators outside it. So
the copies stay hooked up to their original sources instead of arriving bare.

One nuance: some inputs accept several cables at once (a "multi-input"). When a
copied operator feeds one of those, its output is added alongside the original's,
so both feed the target. A plain single input is left untouched on purpose — the
copy shouldn't hijack the cable the original is using.

## Step: Duplicate an operator that is fed from outside the selection

**Action:**
Wire one operator into the input of a second operator. Select only the second
operator and press `Ctrl+Shift+D`.

**Expected:**
- A copy of the second operator appears at the mouse position.
- The copy is wired to the **same** source operator, on the same input — the
  original's cable is untouched.
- The new copy is now the selected operator.

## Step: Compare against plain Duplicate

**Action:**
Undo (`Ctrl+Z`) until you're back to the single wired pair. Select the second
operator and press `Ctrl+D` (plain duplicate).

**Expected:**
- A copy appears, but this time it has **no** cable to the source operator (plain
  duplicate keeps only the wiring that ran between selected operators).
- This shows the difference: `Ctrl+Shift+D` keeps the incoming wiring, `Ctrl+D`
  does not.

## Step: Duplicate two connected operators together

**Action:**
Select two operators that are wired to each other and also take an input from a
third operator you leave unselected. Press `Ctrl+Shift+D`.

**Expected:**
- Both operators are copied and the copies stay wired to each other.
- The incoming cable from the third operator is rebuilt on the matching copy.

## Step: Duplicate an operator feeding a multi-input

**Action:**
Wire an operator into a target whose input accepts several cables of the same
type at once (a multi-input). Select only that feeding operator and press
`Ctrl+Shift+D`.

**Expected:**
- The copy's output is added to the same multi-input, right after the original's
  cable — both the original and the copy now feed the target.
- An input that only takes one cable would **not** pick up the copy's output;
  only multi-inputs gain the extra cable.

## Step: Snapped neighbours are pushed down by the new input row

**Action:**
Build a tidy vertical stack where a multi-input operator has other operators
snapped directly below its input rows. Select an operator feeding that
multi-input and press `Ctrl+Shift+D`.

**Expected:**
- The target gains one new input row for the added cable, and the operators
  snapped below that row slide down by one row to make space — just as they would
  if you dragged a cable onto an existing one by hand.
- The stack stays neatly snapped; nothing overlaps.
- `Ctrl+Z` removes the copy and slides the pushed operators back up, all in one
  undo step.

## Step: Undo restores the original graph

**Action:**
After any of the duplications above, press `Ctrl+Z` once.

**Expected:**
- The copied operators and every cable added for them disappear in a single step.
- The graph looks exactly as it did before you duplicated.

## Step: Duplicate With Connections from the context menu

**Action:**
Right-click a selected operator that is fed from outside the selection and choose
**Duplicate With Connections** from the context menu.

**Expected:**
- Same result as the `Ctrl+Shift+D` shortcut: the copy appears with its incoming
  cable kept.
