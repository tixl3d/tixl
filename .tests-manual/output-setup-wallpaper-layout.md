---
id: output-setup-wallpaper-layout
title: Output Setup — Wallpaper Layout
scope: output-window
tags: [projection-mapping]
added: 2026-10-25
added-in-version: 4.4
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - The Graph window, the Parameter window and one output window in setup mode with its Flow Outliner shown are visible, the canvas on Board.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers **Create Wallpaper Layout**: one content strip cut into a slice per wall, in the order the walls
stand around the room, at the pixel density they are projected at.

## Step: A strip over three walls

**Action:**
1. Add a floor plan from the outliner's `+` (**Add Floor Plan...**), Width `10`, Depth `8`.
2. On the plan card, tick **Edge 1**, **Edge 2** and **Edge 3**.
3. Fence-select "Wall 1", "Wall 2" and "Wall 3" in SURFACES, right-click one of them and choose
   **Create Wallpaper Layout**.

**Expected:**
- A SendToOutput op named "Wallpaper" appears in the open composition, selected, with Resolution
  `11200 × 1600` (28 m of wall, 4 m high, at the walls' own 400 px/m).
- CONTENT lists "Wallpaper", selected, with three slices named "Wall 3", "Wall 2" and "Wall 1". Its card
  shows them side by side across the full height in that order, Wall 3's on the left.
- Each wall shows its slice: lines run from the three slices to "Wall 3", "Wall 2" and "Wall 1".

## Step: Undo takes back op and slices together

**Action:**
Press Ctrl+Z, then Ctrl+Shift+Z.

**Expected:**
- After undo: the "Wallpaper" op, its CONTENT item and the slices are gone in one step; the walls show
  nothing.
- After redo: all of it is back, the walls showing their slices again.

## Step: A lifted wall raises the strip

**Action:**
1. Select "Wall 2" and lift it off the ground by `1` m on the plan card.
2. Select the three walls, Ctrl-click "Wallpaper" in CONTENT to add it to the selection, right-click
   "Wall 1" and choose **Create Wallpaper Layout on Wallpaper**.

**Expected:**
- No second op is added. The op's Resolution becomes `11200 × 2000`: the strip now spans 5 m of height.
- Wall 2's slice covers the top 4 m of the strip, Wall 1's and Wall 3's the bottom 4 m.
- The walls keep the same slices as before (no new slice rows appear), only their rects moved.

## Step: Density follows the canvas

**Action:**
1. Add an output from the OUTPUTS `+`, set its resolution to `1920 × 1080`, and choose **Add Patch** from
   its menu. Rename the patch to `Wall 1`.
2. Right-click the floor plan card and choose **Create Wallpaper Layout** (the menu offers no "on
   Wallpaper" here: the plan is not part of the selection).

**Expected:**
- A second "Wallpaper" op is added for all three walls. Wall 1 lands on a 960 × 540 px patch, 135 px per
  metre of its 4 m height, so the strip renders at 135 px/m: Resolution `3780 × 675`.
- The floor surface is not part of the strip.

## Step: Update from the content's own menu

**Action:**
1. Undo until only the first "Wallpaper" op is left, then widen "Wall 1" by `1` m on its card.
2. Click empty space to clear the selection, right-click "Wallpaper" in CONTENT and choose
   **Update Wallpaper Layout**.

**Expected:**
- The menu offers **Update Wallpaper Layout** without any wall selected; a content item whose slices show
  on fewer than two walls doesn't offer it.
- The Resolution grows by 400 px in width, and Wall 1's slice widens; the slices and their routes stay.

## Step: Arrange Walls follows the strip

**Action:**
Fence-select the three walls, right-click one of them and choose **Arrange Walls**.

**Expected:**
- The cards line up on the Board as Wall 3, Wall 2, Wall 1 from left to right, the order of the slices on
  the "Wallpaper" card.

## Step: Hover and delete

**Action:**
1. Hover "Wallpaper" in CONTENT, then one of its slices, then its card on the Board.
2. Right-click "Wallpaper" in CONTENT and choose **Delete**, then press Ctrl+Z.

**Expected:**
- Each hover blinks the "Wallpaper" op in the graph.
- Delete removes the op, its CONTENT item and its slices; the walls show nothing.
- Undo brings back the op together with its slices, and every wall shows its slice again.

## Step: A surface dropped on a layout patch links to the patch

**Action:**
1. Add a second patch to the output from the earlier step and rename it to `Wall 2`; leave it without content.
2. Drag "Wall 2" from SURFACES onto that patch row.

**Expected:**
- The connection line runs from "Wall 2" to the "Wall 2" patch row, not to the output row.
