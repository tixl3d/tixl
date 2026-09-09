---
id: output-setup-content-crop
title: Output Setup — Content Crop and Pan
scope: output-window
tags: [projection-mapping]
added: 2026-09-09
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one SendToOutput showing a recognisable image, "Surface 1" showing its slice and mapped to output "P1", and a region "Region 1" inside the surface showing the same slice (Surface 1's context menu → Add region, then drag "Slice 1" onto the region).
  - The graph window and one output window with the Flow Outliner shown are visible; content preview is on.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the content-aware edge crop, the Alt pan and the copy-on-write clone of shared slices:
a plain crop never moves the pixels on the wall, only the window over them; Ctrl keeps the old
stretch; Alt slides the content under a fixed region.

## Step: A plain edge crop keeps the picture in place

**Action:**
On the Board, select "Surface 1" and drag its right edge handle (square) about a third of the
way to the left. Watch the picture on the card. Then Ctrl+Z.

**Expected:**
- The card narrows and the picture is cut off at the right: what remains visible is exactly
  the left part that was there before, undistorted. Nothing slides or squeezes.
- The slice sub-rect on the "SendToOutput" card shrinks on its right accordingly.
- Undo restores both the size and the slice.

## Step: Ctrl still stretches

**Action:**
Hold Ctrl and drag the same right edge handle (now a circle) a third of the way to the left.
Then Ctrl+Z.

**Expected:**
- The card narrows and the whole picture squeezes into it; the slice rect on the content card
  does not change.

## Step: The same in the Straight view

**Action:**
Switch to **Straight** with "Surface 1" selected. Drag the top edge handle down; then Ctrl-drag
it back up. Ctrl+Z twice.

**Expected:**
- Plain: the top of the picture is cut away, the rest stays put; the slice on the content card
  loses its top.
- Ctrl: the picture re-fits (stretches) into the changed frame; the slice is unchanged.

## Step: Region crop and pan

**Action:**
1. Select "Region 1" and drag its left edge to the right.
2. Hold Alt, press inside the region's body and drag it 100 px to the right, release.
3. Try the Alt-drag again, far past the point where the content would leave the source.

**Expected:**
- After 1: the region's window shrinks over unmoved pixels, like the surface did.
- After 2: the region stays where it is; its picture slides right, revealing what lies to its
  left in the source. The slice rect on the content card moves left by the same fraction.
- After 3: the slide stops at the source's edge; the window never shows outside the texture.

## Step: A shared slice is cloned on crop

**Action:**
1. In the outliner, note that "Slice 1" is shown by both "Surface 1" and "Region 1" (hover it:
   both curves light up).
2. Select "Region 1" and crop its right edge.
3. Look at the CONTENT column and at "Surface 1".

**Expected:**
- After 2: a new slice ("Slice 2") appears under "SendToOutput"; "Region 1" now shows it and
  "Surface 1" still shows "Slice 1", unchanged.
- Ctrl+Z removes the clone again and "Region 1" is back on "Slice 1" at its old size.

## Step: Nothing to crop, nothing breaks

**Action:**
Clear "Surface 1"'s content (context menu → Clear content inputs) and crop an edge.

**Expected:**
- The rectangle crops as before; no slice is created or changed. Undo restores the content.
