---
id: symbol-browser-presets
title: Symbol Browser Presets
scope: graph-window
tags: [user]
added: 2026-10-28
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
  - An operator with presets exists, e.g. [LinePoints].
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks choosing a preset while inserting an operator with the symbol browser.

## Step: Listing presets after the name

**Action:**
With the Graph Window focused, press `Tab`, type `linep` and look at the faint text after it, then press `Space`.

**Expected:**
- Before the space, the faint text completes the name and shows `Space presets`.
- After the space, the list shows the operator's presets with "Default" first.

## Step: Inserting with a preset

**Action:**
Type the first letters of a preset, use `Down` to pick another one, and press `Enter`.

**Expected:**
- The operator is inserted with the picked preset applied.
- One `Ctrl+Z` removes the operator together with the preset.

## Step: Inserting and editing

**Action:**
Press `Tab`, type the operator name, `Space`, a preset filter, and another `Space`.

**Expected:**
- The operator is inserted with the preset and the parameter popover opens for it.

## Step: Presets for an operator picked with the arrow keys

**Action:**
Select an operator, press `Tab` without typing, pick a suggested operator with `Down`, and press `Space`.

**Expected:**
- The faint text shows `Space <Operator> presets` before pressing it.
- The list switches to the picked operator's presets instead of emptying.

## Step: Tab without hovering the graph

**Action:**
Click the graph background, move the mouse over another window without clicking, and press `Tab`.

**Expected:**
- The symbol browser opens in the graph, in the middle of the view if nothing is selected.
