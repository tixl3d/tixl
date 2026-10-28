---
id: operator-bookmarks
title: Operator Bookmarks
scope: graph-window
tags: [user]
added: 2026-10-28
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window, with operators in at least two compositions.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks bookmarking operators on the digit keys and jumping back to them.

## Step: Bookmarking an operator

**Action:**
Select an operator, keep the Graph Window focused and press `Shift+1`.

**Expected:**
- A small badge with "1" appears at the operator's top left corner.
- The status line confirms the bookmark.

## Step: Jumping within a composition

**Action:**
Scroll the graph so the operator is out of view, select something else, and press `1`.

**Expected:**
- The bookmarked operator is selected and the view moves to show it.

## Step: Jumping across compositions

**Action:**
Open another composition (e.g. with `I` on an operator) and press `1`.

**Expected:**
- The graph returns to the bookmarked operator's composition and selects it.

## Step: Moving and removing a bookmark

**Action:**
With the bookmarked operator selected, press `Shift+2`, then `Shift+2` again.

**Expected:**
- After the first press the badge shows "2" and `1` no longer jumps anywhere.
- After the second press the badge disappears.

## Step: Bookmarks are kept per project

**Action:**
Bookmark an operator, restart the editor and open the same project, then press its digit.

**Expected:**
- The badge is still shown and the digit jumps to the operator.
- In a different project the digit does nothing.
