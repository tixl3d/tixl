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

## Step: Moving the card moves the room

**Action:**
Drag "Floor Plan 1"'s frame (not a corner) about 3 m to the right and release.

**Expected:**
- "Wall 1"'s Position (m) x and "Floor"'s grow by the same amount; nothing else changes.
