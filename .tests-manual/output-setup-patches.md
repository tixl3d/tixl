---
id: output-setup-patches
title: Output Setup — Patches (the direct pipe)
scope: output-window
tags: [projection-mapping]
added: 2026-09-05
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - Three windows are visible - the graph window, the Parameter window, and one output window with its Flow Outliner shown (it opens with a selected SendToOutput; otherwise the toolbar's list icon).
  - A LoadImage op (any image) exists in the graph.
---

Covers Patches: an output's direct pipe is a list of **patches**, each a rectangle of
output pixels fed by one slice. Patches composite underneath any surfaces mapped to the
same output. The second half covers editing patches on the output canvas, the Split
helpers, and promoting a patch to a surface.

## Step: Build the test setup

**Action:**
1. Connect the LoadImage op to a new **SendToOutput** op (`+` on **CONTENT** creates one
   to the right of a selected texture op).
2. Click `+` on **OUTPUTS**.

**Expected:**
- A "SendToOutput" item under CONTENT and a **"P1"** item under OUTPUTS, with no chevron on
  P1 (it has no patches yet).

## Step: Dropping content on an output fills it, with no patch to manage

**Action:**
Drag the "SendToOutput" CONTENT item onto the "P1" item.

**Expected:**
- P1 shows the image full-frame in its output view and on its Board card, and a connection
  runs from "Slice 1" straight to the "P1" row.
- **No** child item appears and P1 gains no chevron: an output's sole full-canvas patch is
  the output as far as the UI is concerned, so it is folded into the row (the same way a
  source's full-frame slice folds into the source).
- Dragging the same content onto "P1" again changes nothing.

## Step: The folded patch still has corner handles in Output mode

**Action:**
1. With "P1" selected, switch the mode segmented button to **Output**.
2. Drag one corner handle of the canvas a little way inward.
3. Press Ctrl+Z.

**Expected:**
- After 1: four corner handles sit on the canvas corners, with no label and no second
  outline — the folded full-canvas patch offering its keystone without having to be made
  into a patch first. In Board and Straight it stays invisible as before.
- After 2: the image keystones, and P1 gains a chevron with one listed patch — the drag
  promoted it, because it no longer covers the whole canvas. Its label appears with it.
- After 3: the corner goes back and the patch folds away again.

## Step: The output frame's own context menu

**Action:**
In **Output** mode, right-click bare canvas (not on a patch, surface or label).

**Expected:**
- The output's menu opens — the same entries as right-clicking "P1" in the outliner
  (Add Patch, Split into…, Clear Inputs, Duplicate, Rename, Delete).
- Right-clicking a patch or a mapped surface still opens *that* entity's menu; the frame
  only answers where nothing else is under the cursor.

## Step: Add Patch makes a visible tile

**Action:**
Right-click "P1" → **Add Patch**.

**Expected:**
- P1 gains a chevron and now lists **two** items: the previously folded full-canvas patch and
  the new one, which is a centred quarter of the canvas (visible and draggable on the output
  canvas, not lying exactly on the canvas border).
- Ctrl+Z removes the new patch and folds the remaining one away again.

## Step: Patch geometry edits from the card

**Action:**
With "Patch 1" selected, set **Size (px)** to 960 × 540, then **Position (px)** to
480 × 270. Press Ctrl+Z twice.

**Expected:**
- After the size edit the image occupies the top-left quarter of P1's canvas; after the
  position edit it sits centred.
- Each Ctrl+Z reverts exactly one of the two edits.

## Step: A second patch, added empty and fed by the gutter toggle

**Action:**
1. Right-click "P1" → **Add Patch**.
2. Click the "Slice 1" item under the SendToOutput content.
3. Click the input arrow in the left gutter of the new "Patch 2" item.

**Expected:**
- After 1: "Patch 2" appears dimmed under P1 with no connection; its card says nothing is
  routed yet.
- After 3: the arrow lights up, a connection from "Slice 1" reaches "Patch 2" in the outliner,
  and the item is no longer dimmed. Clicking the arrow again unfeeds it.

## Step: Patches sit under surfaces

**Action:**
1. Click `+` on **SURFACES**, then drag "Surface 1" onto "P1".
2. Drag the "SendToOutput" item onto "Surface 1".

**Expected:**
- P1's output view shows the corner-pinned surface drawn **over** the full-frame patch
  image, not replacing it.

## Step: Patch handles on the output canvas

**Action:**
1. Click "P1" in the outliner so the output window shows its canvas, then click the
   "Patch 1" label on the canvas.
2. Drag the patch's top-right corner inward by about a third of the canvas.
3. Drag the right-edge handle (square, mid-edge) left; then hold Shift and drag it
   again.
4. Drag the "Patch 1" label toward the canvas' bottom-left corner until it stops.
5. Press Ctrl+Z three times.

**Expected:**
- After 1: "Patch 1" is selected in the panel; the canvas shows its frame with round
  corner handles and, as the selected patch, square edge handles.
- After 2: the image keystones (the corner moves freely, the other three stay).
- After 3: the first drag moves only that edge, and it clicks onto the canvas centre
  line when close; with Shift held there is no snapping.
- After 4: the whole patch moves and snaps flush into the canvas corner.
- Each Ctrl+Z reverts one gesture: the move, the edge crop, then the corner.

## Step: Split into a 2×2 matrix

**Action:**
Right-click "P1" → **Split into 2×2**. Then drag the second tile's left-edge handle to
the right by a little and release.

**Expected:**
- P1 now has four items "Patch 1" … "Patch 4", each fed by "Slice 1"; the canvas shows
  the image four times in a 2×2 grid, and the first tile is selected.
- Before the edge drag the tiles share their edges exactly; dragging the second tile's
  left edge opens a gap and, when dragged back, snaps shut against the first tile.

## Step: Use on Surface

**Action:**
Right-click "Patch 1" (in the panel or on its canvas label) → **Use on Surface**.

**Expected:**
- "Patch 1" disappears from P1 and a new surface item appears under SURFACES, selected,
  mapped to P1 and fed by "Slice 1".
- Nothing moves on the canvas: the surface's corner pin sits exactly where the patch
  was, now with the surface's anchor marker and its edge handles.
- The surface card shows Size (m) 1 × 0.625 (the tile's aspect).

## Step: Deleting the slice unfeeds, deleting the patch removes

**Action:**
1. Right-click "Slice 1" → **Delete**.
2. Right-click "Patch 1" → **Delete**.

**Expected:**
- After 1: both patch items stay but turn dimmed; the output view shows only the surface.
- After 2: only "Patch 2" remains under P1. One Ctrl+Z brings "Patch 1" back.

## Step: Clear Inputs on an output

**Action:**
Right-click the "P1" item in the OUTPUTS column and choose **Clear Inputs**; then press Ctrl+Z.

**Expected:**
- Every connection into "P1" disappears: the surface mappings onto it and all of its patches
  (the patch items under it are gone), while the surfaces and their slices remain. The entry
  is greyed out on an output that has no inputs. Ctrl+Z restores everything in one step.
