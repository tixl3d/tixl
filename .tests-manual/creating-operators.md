---
id: creating-operators
title: Creating Operators
added: 2026-04-19
added-in-version: 4.2
scope: graph-window
tags: [user, smoke, essential]
prerequisites:
  - An empty project is open.
  - The Graph Window is visible.
related-help:
  - ../.help/using/graph-window.md
---

A quick walkthrough of adding an operator to the graph. This is the move you'll
make more than any other in TiXL — if any step here feels surprising or awkward,
that's worth noting in the comment box.

## Step: Opening the Symbol Browser

**Action:**
With the [ui:Graph|Graph Window] focused, press `Tab`.

**Expected:**
- The [ui:SymbolBrowser|Symbol Browser] opens.
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

## Step: Inserting after the selected operator

**Action:**
Keep `[RadialGradient]` selected and the mouse over the graph. Hold `Shift`, then release it.

Press `Tab`, type `Blur` and press `Return`.

**Expected:**
- Without `Shift` the output anchor below `[RadialGradient]` shows a `+`; with `Shift` the one on its right does.
- `[Blur]` is snapped below `[RadialGradient]` and connected to its output.

## Step: Inserting before the selected operator

**Action:**
Select `[RadialGradient]` and hold `Ctrl`, then also `Shift`.

With only `Ctrl` held, press `Tab` and press `Return` to pick the first suggestion.

**Expected:**
- Holding `Ctrl` moves the `+` from the output to the input anchor above `[RadialGradient]`; adding `Shift` moves it to the input on its left.
- The editor keeps the focus on the graph; Ctrl+Tab does not switch to another window.
- The Symbol Browser only suggests operators whose output fits the first input.
- The new operator is snapped above `[RadialGradient]` and connected to its first input.

## Step: Inserting into a snapped stack

**Action:**
Select `[Blur]` (snapped below `[RadialGradient]`), press `Ctrl` + `Tab`, type `Tint` and press `Return`.

**Expected:**
- Holding `Shift` does not move the `+`: a snapped input can only be split along its snap.
- `[RadialGradient]` moves up by one row and `[Tint]` sits between it and `[Blur]`, connected to both.
