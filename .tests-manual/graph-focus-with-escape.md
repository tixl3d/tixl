---
id: graph-focus-with-escape
title: Returning Keyboard Focus to the Graph with Esc
scope: graph-window
tags: [user]
added: 2026-11-06
added-in-version: 4.4
prerequisites:
  - A project you can edit is open, with the Graph, Parameter and Output windows visible.
  - A graph with a few operators, one of them selected.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks that `Esc` gives the keyboard focus back to the graph from other windows, but only once nothing
else in that window needs the key.

## Step: Returning from another window

**Action:**
Click an empty area of the Parameter Window, then press `Esc`, then `Ctrl + D`.

**Expected:**
- After `Esc` the Graph Window shows its focus frame.
- `Ctrl + D` duplicates the selected operator.

## Step: Cancelling a text field first

**Action:**
Click into a number field in the Parameter Window, type a new value and press `Esc` once. Then press `Esc` again.

**Expected:**
- The first `Esc` reverts the value and leaves the focus in the Parameter Window.
- The second `Esc` moves the focus to the graph.

## Step: Closing a popup first

**Action:**
Open the main menu or a right-click menu in the Parameter Window and press `Esc`.

**Expected:**
- The menu closes and the focus does not jump to the graph.

## Step: Parameter popover

**Action:**
In the graph, press `S` to open the parameter popover, then press `Esc` until it closes.

**Expected:**
- `Esc` steps back through the popover as before, and the popover closes normally.

## Step: Leaving and reentering an operator

**Action:**
Select an operator that has children (not a plain C# operator), move the mouse over the Parameter Window and press `Esc`. Then press `Alt + Down`, `Alt + Up` and `Alt + Down` again without moving the mouse.

**Expected:**
- `Alt + Down` opens the selected operator, even with the mouse outside the graph.
- `Alt + Up` returns to the parent and selects the operator you left.
- The second `Alt + Down` opens that same operator again.

## Step: Sketch keeps Esc

**Action:**
Select a [Sketch] operator shown in the Output Window, hover the Output Window, draw a stroke, select it and press `Esc`.

**Expected:**
- `Esc` clears the sketch selection and the focus stays in the Output Window.

## Step: Output setup tools

**Action:**
Open the output setup, arm the line tool (or start drawing a floor plan), press `Esc`, then press `Esc` again.

**Expected:**
- The first `Esc` stops the tool and keeps the focus in the Output Window.
- The second `Esc` moves the focus to the graph.

## Step: Shift + Esc is unchanged

**Action:**
With the focus in the Parameter Window, press `Shift + Esc`, then press it again.

**Expected:**
- The UI elements are hidden and shown again; the focus does not move to the graph.
