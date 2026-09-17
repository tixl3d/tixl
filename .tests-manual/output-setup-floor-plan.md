---
id: output-setup-floor-plan
title: Output Setup — Floor Plan
scope: output-window
tags: [projection-mapping]
added: 2026-09-15
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - The Parameter window and one output window in setup mode with its Flow Outliner shown are visible, the canvas on Board.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the **Floor Plan** card: a footprint on the Board whose edges carry wall surfaces, with the
walls' width and stage pose derived from it. There is no stage view yet, so poses are checked
through the numbers on the surface cards.

## Step: Add a floor plan

**Action:**
1. Click the `+` at the right of the outliner's header row and choose **Add Floor Plan...**.
2. Set Width `10`, Depth `8`, leave **With floor surface** ticked, click **Create**.

**Expected:**
- A "Floor Plan 1" card appears on the Board below the floor line: a 10 × 8 m rectangle with a light
  fill, thin edges, a square handle on each corner, and "10×8 m · closed" above it.
- SURFACES lists one item, "Floor". Its card shows Rotation (°) `0 -90 0` read-only, Size (m)
  `10 8`, and a line saying it is the floor of Floor Plan 1.
- The Parameter window shows the plan's card: **Closed** ticked, **Floor surface** ticked, Wall height
  `4`, and four rows "Edge 1 · 10 m", "Edge 2 · 8 m", "Edge 3 · 10 m", "Edge 4 · 8 m", each with an
  unticked box.

## Step: Raise walls on edges

**Action:**
1. On the plan card, tick the box of **Edge 1** and of **Edge 3**.
2. Select "Wall 1" in SURFACES.

**Expected:**
- After 1: the two edges draw thick and green on the Board with "Wall 1 · 10 m" and "Wall 3 · 10 m"
  written along them; SURFACES lists "Wall 1" and "Wall 3"; the rows now read "Wall 1 · 10 m" and
  "Wall 3 · 10 m".
- After 2: the card shows Size `10 4`, Rotation read-only, and Wall 1's yaw differs from Wall 3's by
  180 (they face each other across the room).

## Step: Corners drive the walls

**Action:**
1. Drag the plan's top-right corner handle about 2 m to the right, then release.
2. Press Ctrl+Z.

**Expected:**
- After 1: while dragging, the far edge's label updates its length; afterwards "Wall 3"'s Size (m)
  width equals the new edge length and "Floor"'s Size follows the new bounding box.
- After 2: corner, wall width and floor size all return in one step.

## Step: Typed width writes back to the plan

**Action:**
Select "Wall 1" and type `12` into the first Size (m) field.

**Expected:**
- The plan's second corner moves so Edge 1 is 12 m long, the Board label reads "Wall 1 · 12 m", and
  the floor grows to 12 m wide. The wall's height stays 4.

## Step: Taking a wall down removes it, unless it is in use

**Action:**
1. Select "Floor Plan 1" and untick the box of **Wall 3**, then tick it again, then untick it again.
2. Tick **Edge 3** once more, drag the "SendToOutput" CONTENT item onto the new "Wall 3" in SURFACES,
   select "Floor Plan 1" and untick **Wall 3**.
3. Select "Wall 3".

**Expected:**
- After 1: the edge draws thin, its row reads "Edge 3 · …", and SURFACES lists no "Wall 3" — a
  wall nothing was done to is removed, and toggling leaves nothing behind.
- After 2: the edge draws thin again but SURFACES still lists "Wall 3", since content was routed to
  it; its row reads "Edge 3 · … · Wall 3 lowered".
- After 3: its Position and Rotation are editable and its card no longer mentions the plan. Ticking
  Edge 3 again on the plan's card brings this same "Wall 3" back, with its content.

## Step: Start a plan from a surface

**Action:**
1. Click `+` on SURFACES; set the new surface's Size (m) to `6` × `3`.
2. Right-click it in the outliner and choose **Start Floor Plan from Bottom Edge**.

**Expected:**
- A "Floor Plan 2" card appears under the surface's card: an open run of two corners 6 m apart with
  one thick edge labelled with the surface's name and "6 m"; its card shows **Closed** unticked, no
  floor row, and one edge row ticked.
- The surface's card now says it is the wall on edge 1 of Floor Plan 2, with Rotation read-only.

## Step: Moving the card leaves the stage alone

**Action:**
Note "Wall 1"'s Position (m), then drag "Floor Plan 1"'s frame (not a corner) about 3 m to the
right and release.

**Expected:**
- The card moves; "Wall 1"'s Position (m) and "Floor"'s are unchanged — the plan's corners are stage
  metres, the card's place on the Board is only presentation.

## Step: Draw walls from an open run

**Action:**
1. With "Floor Plan 2" (the open run from the previous step) selected, click the plus that sits just
   past its right end on the Board.
2. Move the mouse straight up from that corner; watch the preview line, then type `4`.
3. Click.
4. Move the mouse left until the preview points at the run's first corner and the line reads
   "close", then click.

**Expected:**
- After 1: a line follows the cursor from the run's right end, with its length written along it.
- After 2: the line snaps to 90° from the first wall and locks to 4 m, labelled "4 m", whatever the
  cursor's distance.
- After 3: a second corner and a "Wall 2" surface appear; SURFACES lists it; the tool stays active.
- After 4: the plan closes (its card now shows **Closed** ticked and a **Floor surface** row), a
  "Wall 3" stands on the closing edge, and the tool ends. Ctrl+Z three times undoes the close and
  the two walls one step each.

## Step: Free angles and ending the tool

**Action:**
1. Select "Floor Plan 2", open its context menu in the outliner or on its card and choose
   **Draw Walls**. It is disabled, since the plan is closed; instead untick **Closed** on its card and
   choose **Draw Walls** again.
2. Hold Shift and move the mouse: the line follows the cursor freely. Release Shift: it snaps to 45°
   steps. Press Escape.

**Expected:**
- After 1: the preview line appears from the run's last corner.
- After 2: the preview disappears on Escape and clicking on the Board selects cards again.

## Step: The room in the graph

**Action:**
1. In the graph, add **StageGeometry**, connect it to a **GeometryToMesh** and that to a **DrawMesh**;
   show the DrawMesh in an output window in operator mode.
2. On StageGeometry, set **UvMode** to OutputCanvas. Put a **SetMaterial** after the DrawMesh with the
   pixel-map image (a LoadImage of the same file) as its BaseColorMap. Set the output window's camera
   to look at the room.

**Expected:**
- After 1: the floor and the walls appear as flat quads standing where the Board placed them, the
  walls facing the room's inside.
- After 2: each wall shows the part of the pixel map that its patch or mapping covers on the output
  canvas, the right way up, and changing the plan on the Board moves the quads at once.

## Step: Edges and corners

**Action:**
1. On a closed rectangular plan with walls on every edge, drag the small dot in the middle of the
   top edge upward by about 2 m and release.
2. Right-click the dot in the middle of the right edge and choose **Split Edge**. Then click the dot
   of the new second half once, and once more.
3. Right-click the new corner and choose **Remove Corner**.
4. Right-click any corner of a plan with only three corners.

**Expected:**
- After 1: the top edge moves up, staying parallel; the left and right walls grow by 2 m and their
  labels say so; the top wall keeps its width. Ctrl+Z puts it back in one step.
- After 2: a corner appears in the middle of the right edge and a new wall stands on the second half,
  so the room stays closed. The first click takes that wall down (the dot turns hollow), the second
  raises it again.
- After 3: the corner is gone, the right edge is one segment again with one wall; the other wall's
  surface is gone too if it had no content, or stays in SURFACES if it had.
- After 4: **Remove Corner** is disabled.

## Step: A scanned plan as the tracing backdrop

**Action:**
1. Drop a plan image onto the Board (any image with a straight edge of known length will do).
2. Right-click its card and choose **Set Scale...**; click two points about a third of the image
   apart along one edge; in the prompt type `6` and click **Apply**.
3. On the image's card in the Parameter window, tick **Locked**.
4. Try to drag the image card, then start a fence across it.

**Expected:**
- After 2: the card resizes so the drawn line spans 6 m against the Board's metre grid; the line
  stays on the card labelled "6 m" with a handle at each end; the card reads "… mm per pixel".
- After 3: a lock icon appears beside the card's name.
- After 4: the card neither moves nor gets selected; a fence started over it selects only what
  lies on top of it. Unticking **Locked** restores both.

## Step: Closing a run by dropping a corner

**Action:**
On an open run with at least three corners, drag its last corner onto its first and release.

**Expected:**
- The two corners merge, the plan's card shows **Closed** ticked, and a wall stands on the edge
  that now joins them. Ctrl+Z reopens the run in one step.
