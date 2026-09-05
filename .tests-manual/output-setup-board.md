---
id: output-setup-board
title: Output Setup — The Board
scope: output-window
tags: [projection-mapping]
added: 2026-09-05
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one SendToOutput with content, one surface mapped to an output, one patch, one prop and one reference image (the "Output Setup — Patches" and "Properties in the Parameter Window" sets build most of this).
  - The graph window, the Parameter window and one output window with the Flow Outliner shown are visible.
---

Covers Phase C.1 and C.3 of the UI restructuring: the **Board**, the 2D overview every entity
lives on — metres, Y up, a floor line at 0 and a metric grid — with cards for every kind, a
seeded layout, fence and group selection, drag-to-place and presentation scaling. Selecting
never leaves the Board; only a double-click enters an entity's space. Fading between spaces
(C.2) and the collapse of the per-kind canvases (C.4) are later slices.

## Step: The Board is the home view

**Action:**
1. Click the SendToOutput op in the graph window (the outliner opens), then click an empty spot
   of the outliner's body so nothing is selected.
2. Click the "Surface 1" item in the SURFACES column, then the "Image 1" item in the REFERENCE
   IMAGES shelf, then the "SendToOutput" item in the CONTENT column, then the "Slice 1" item
   under it.
3. Click the **Straight** tab with "Surface 1" selected, then the **Board** tab.

**Expected:**
- After 1: the output area shows the Board: a metre grid with "n m" labels that steps 1 → 5
  → 10 as you zoom (like the curve editor's), a stronger horizontal **Floor (0 m)** line, and
  cards laid out on it — the reference image at the far left, the content card with its live
  texture and the slice drawn as a labelled sub-rect, "Surface 1" standing on the floor at
  its metre size with "1×1 m" beside its name, the output card at the right with the live
  composite and "Patch 1" drawn inside it, and a stick figure labelled "1.7 m" standing on
  the floor. The header reads the setup's name and a segmented control whose first tab,
  **Board**, is active.
- After 2: the Board stays on screen for every click; only the highlighted card changes (the
  slice highlights its sub-rect). No "Set a photo path" message appears for the image.
- After 3: the Straight canvas opens for the surface; the Board tab brings the Board back with
  "Surface 1" highlighted and the same layout as before.

## Step: Zoom range

**Action:**
Scroll the mouse wheel over the Board until the content card fills the window, then keep
zooming in on one corner of it; then zoom all the way out.

**Expected:**
- Zooming in continues well past the card filling the window — down to centimetre grid
  lines with "n cm" labels — and does not stop early. Zooming out stops once the whole
  layout is a few pixels wide.

## Step: Cards select and drag

**Action:**
1. Click the content card, then Ctrl+click the output card.
2. Drag "Surface 1" by its body 1 m to the right (watch the grid), release, press Ctrl+Z.
3. Drag the figure onto the surface, then Ctrl+Z.
4. Click an empty spot of the Board.

**Expected:**
- After 1: both cards show the selection outline; the outliner items match; the Parameter
  window shows the card of the primary.
- After 2: the surface follows the cursor and stays where it is dropped; the undo puts it back
  in one step. The output canvas (Output tab) is unchanged by the move — the corner pin did not
  move.
- After 3: the figure moves and undo returns it.
- After 4: nothing is selected and the Board stays.

## Step: Fence and group drag

**Action:**
1. Drag from an empty spot left of the content card to an empty spot below the surface, so the
   rectangle touches the content card and "Surface 1"; release.
2. Shift+drag a rectangle touching the output card; then Ctrl+drag one touching the content
   card.
3. With the output card and "Surface 1" selected, press on "Surface 1"'s body and drag 1 m
   to the right; release; press Ctrl+Z.
4. Click "Surface 1" once (no drag).

**Expected:**
- After 1: while dragging, a translucent rectangle is drawn and the cards it touches light up
  live; on release the content card and "Surface 1" are selected, the outliner items match.
- After 2: the output card joins the selection; the content card leaves it.
- After 3: both the output card and the surface move together and stay where dropped; one
  Ctrl+Z returns both.
- After 4: only "Surface 1" is selected.

## Step: Edge handles crop the surface

**Action:**
1. Select "Surface 1" on the Board. Drag the square handle at the middle of its right edge
   0.5 m to the right (watch the grid), release; open the Output tab and look at the corner
   pin; press Ctrl+Z.
2. Back on the Board, hold Ctrl and drag the same handle 0.5 m to the right, release, then
   Ctrl+Z.

**Expected:**
- After 1: the card grows to the right while its left edge and the anchor stay put; its
  metadata reads "1.5×1 m" after release; on the Output tab the surface's quad covers a
  wider area of the projector canvas. The undo restores both in one step.
- After 2: the card grows the same way but the metadata stays "1×1 m" (a stretch, not a
  crop); the undo restores it.

## Step: Focus key

**Action:**
1. Select the figure, move the mouse over the Board and press **F** (the Focus Selection key).
2. Click an empty spot of the Board and press F again.

**Expected:**
- After 1: the view eases so the figure fills most of the window.
- After 2: the view eases back to frame every card.

## Step: Presentation scale of a pixel card

**Action:**
Select the content card and drag its square top-right handle inward until the card is about half
its width. Then open the Output tab and check the send's Resolution in the Parameter window.

**Expected:**
- The card shrinks keeping its aspect; its "1920×1080" metadata does not change, nor does the
  op's resolution or the patch on the output. Hovering the handle explains that it is
  presentation only.

## Step: Double-click enters a space

**Action:**
1. Double-click the output card, then press the Board tab; double-click the surface card, then
   the Board tab.
2. Double-click the content card, then click the **Board** button at the left of the canvas
   header.
3. Double-click the reference image card, set a path in its header field if none is set, then
   click its **Board** button.

**Expected:**
- After 1: the output card opens the Output canvas with the projector composite; the surface
  card opens the Straight canvas. The Board tab returns to the same Board pan/zoom each time.
- After 2: the source canvas opens with the slice laid out on the texture; the Board button
  returns to the Board with the content card selected.
- After 3: the reference view opens (photo, trace buttons); the Board button returns to the
  Board, where the image card now shows the photo. The Parameter window's Reference Image
  card offers the same path field with a file picker.

## Step: Reference images come from the asset system

**Action:**
1. Drag a JPG from Windows Explorer onto an empty spot of the Board and drop it.
2. Open the Asset Library, drag one of the project's images onto the Board.
3. Select the new "Image 1"-style card, and in the Parameter window click into the **Image**
   field and type part of another image's name.

**Expected:**
- After 1: while hovering, a chip "Add as reference image" follows the cursor. On drop a new
  reference image card with the photo appears where it was dropped, selected; the file now
  exists under the project's Assets/images/reference folder and is listed in the Asset
  Library. Ctrl+Z removes the card again (the file stays).
- After 2: a card appears the same way, pointing at the existing asset — no copy is made.
- After 3: a type-ahead list offers only image assets (png, jpg, ...); picking one swaps the
  card's photo.

## Step: Layout persists

**Action:**
Move any card, then switch setups via the header's setup switcher and back (or reopen the
project).

**Expected:**
- The moved card is where it was left; the other setup has its own seeded layout.
