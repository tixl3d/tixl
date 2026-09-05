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
- A "SendToOutput" row under CONTENT and a **"P1"** row under OUTPUTS, with no chevron on
  P1 (it has no patches yet).

## Step: Dropping content on an output creates a full-canvas patch

**Action:**
Drag the "SendToOutput" CONTENT row onto the "P1" row.

**Expected:**
- P1 gains a chevron and one child row **"Patch 1"** with the status "Slice 1".
- The output view of P1 shows the image full-frame.
- The Parameter window (after clicking "Patch 1") shows a **Patch** card: the line
  "Shows Slice 1 on P1", **Position (px)** 0 × 0 and **Size (px)** 1920 × 1080.

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
2. Click the "Slice 1" row under the SendToOutput content.
3. Click the input arrow in the left gutter of the new "Patch 2" row.

**Expected:**
- After 1: "Patch 2" appears dimmed under P1 with no status; its card says nothing is
  routed yet.
- After 3: the arrow lights up, "Patch 2" reads "Slice 1" and is no longer dimmed.
  Clicking the arrow again unfeeds it.

## Step: Patches sit under surfaces

**Action:**
1. Click `+` on **SURFACES**, then drag "Surface 1" onto "P1".
2. Drag the "SendToOutput" row onto "Surface 1".

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
- P1 now has four rows "Patch 1" … "Patch 4", each fed by "Slice 1"; the canvas shows
  the image four times in a 2×2 grid, and the first tile is selected.
- Before the edge drag the tiles share their edges exactly; dragging the second tile's
  left edge opens a gap and, when dragged back, snaps shut against the first tile.

## Step: Use on Surface

**Action:**
Right-click "Patch 1" (in the panel or on its canvas label) → **Use on Surface**.

**Expected:**
- "Patch 1" disappears from P1 and a new surface row appears under SURFACES, selected,
  mapped to P1 and fed by "Slice 1".
- Nothing moves on the canvas: the surface's corner pin sits exactly where the patch
  was, now with the surface's anchor marker and its edge handles.
- The surface card shows Size (m) 1 × 0.625 (the tile's aspect).

## Step: Deleting the slice unfeeds, deleting the patch removes

**Action:**
1. Right-click "Slice 1" → **Delete**.
2. Right-click "Patch 1" → **Delete**.

**Expected:**
- After 1: both patch rows stay but turn dimmed; the output view shows only the surface.
- After 2: only "Patch 2" remains under P1. One Ctrl+Z brings "Patch 1" back.
