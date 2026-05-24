---
id: creating-operators
title: Creating Operators
scope: graph-window
tags: [smoke, essential]
prerequisites:
  - An empty project is open.
  - The Graph Window is visible.
related-help:
  - ../.help/using/graph-window.md
---

A short tour of how to spawn an operator on the graph. This is the most common
flow in TiXL — if any step here feels surprising, that's worth noting in the
comment box.

## Step: Opening the Symbol Browser

**Action:**
With the Graph Window focused, press `Tab`.

**Expected:**
- The Symbol Browser opens.
- Its search field is focused and empty — you can start typing immediately.

## Step: Finding an operator by name

**Action:**
With the Symbol Browser open, type `RG` (the search is case-insensitive and
matches partial names).

**Expected:**
- The result list filters down to operators whose names contain those letters.
- `[RadialGradient]` is visible somewhere in the list.

## Step: Creating an operator

**Action:**
With the search results visible, use the cursor up/down keys to highlight
`[RadialGradient]`.

Press `Return` — or click the entry with the mouse — to place it.

**Expected:**
- A `[RadialGradient]` operator appears on the graph at the cursor position.
- The new operator is selected.
- Its inputs are shown in the Parameter Window.
- The Symbol Browser closes.
