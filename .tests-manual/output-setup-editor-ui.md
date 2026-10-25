---
id: output-setup-editor-ui
title: Output Setup — Editor UI as Content
scope: output-window
tags: [projection-mapping]
added: 2026-10-25
added-in-version: 4.4
prerequisites:
  - A writable project is open whose active setup is empty.
  - A second display is attached (a projector, TV or monitor in "extend" mode).
  - One output window is visible with its Flow Outliner shown (the toolbar's "Output Setup" button at its right end).
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers mirroring the editor window onto a display through the Output Setup, which replaced the old
2nd render window.

## Step: The CONTENT + offers the editor UI

**Action:**
Click `+` on **CONTENT**.

**Expected:**
- A menu opens with **SendToOutput Op** and **Editor UI**.
- Hovering each entry shows a tooltip.

## Step: Adding the editor UI

**Action:**
Choose **Editor UI**, then click `+` on **CONTENT** again.

**Expected:**
- An "Editor UI" row appears under CONTENT, selected, and the Parameter window says it mirrors the
  editor window with the window's size in pixels.
- Its Board card shows a live picture of the editor.
- In the second menu, **Editor UI** is disabled: there is only one.

## Step: Showing the UI on a display

**Action:**
Drag the "Editor UI" row onto the second display under **LOCAL BINDINGS**.

**Expected:**
- An output is created and bound, and the second display goes fullscreen showing the editor.
- The editor fills the display at its own aspect, with black bars where the shapes differ — not stretched.
- The output gets a visible patch row (a fitted patch), and its Scale mode in the Parameter window is **Fit**.
- Moving a window or opening a menu in the editor shows up on the display right away.
- The mirrored picture is not transparent or washed out anywhere, including behind panels and menus.

## Step: Showing only the graph

**Action:**
1. Right-click the "Editor UI" row → **Add slice**.
2. Click the new slice in the CONTENT column, then drag its edges on the "Editor UI" card until it covers
   only the graph area.
3. Drop the slice onto the output's patch row.

**Expected:**
- The display shows only the cropped part of the editor.

## Step: Renaming and deleting

**Action:**
1. Rename the "Editor UI" row to "Graph View".
2. Undo with `Ctrl+Z`.
3. Right-click the row → **Delete**.
4. Undo with `Ctrl+Z`.

**Expected:**
- After 1: the row reads "Graph View", and its slices are named after it.
- After 2: it reads "Editor UI" again.
- After 3: the row and its slices are gone, and the display shows black.
- After 4: everything is back, and the display shows the editor again.

## Step: Survives a reload

**Action:**
Close and reopen the project.

**Expected:**
- The "Editor UI" row is still listed under CONTENT, and the display shows the editor again once the
  project is open.
- It is not removed when switching to another composition in the graph.
