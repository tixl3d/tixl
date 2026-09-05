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

## Step: Content preview opacity

**Action:**
With a traced surface that shows a slice, drag the "Content" percent field in the canvas header
down to 0%, then up to 100% (or double-click it and type).

**Expected:**
- At 0% the traced quad on the image card and the surface card show only the photo; at 100%
  the slice covers both fully; in between it blends. The value survives a restart.

## Step: Scaling a surface on the Board

**Action:**
1. Select "Surface 1" on the Board and drag the round handle at its top-right corner outward
   until the card is about twice as wide; release; Ctrl+Z.
2. Hold Ctrl, then drag the right edge handle (it turns into a circle) 0.5 m to the right;
   release; Ctrl+Z.
3. Without Ctrl, drag the same edge handle (a square) 0.5 m to the right; Ctrl+Z.

**Expected:**
- After 1: the surface doubles in both dimensions, its aspect kept; a region inside it and any
  measuring lines scale with it, and on the Output tab its corner-pin quad covers a
  correspondingly larger area. The metadata reads the new size while dragging. Undo restores
  everything in one step.
- After 2: only the width grows; regions and lines stretch along X; the green traced quad on
  the image card stays where it was (the wall in the photo did not change).
- After 3: the crop from the earlier step: the width grows, the raster spacing and the
  regions stay put, and the traced quad on the image card crops along.


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
3. Double-click the reference image card (pick an image in the Parameter window first if it
   has none), then click its **Board** button.

**Expected:**
- After 1: entering is one continuous move on the same canvas, about half a second: the view
  zooms onto the output card, the other cards fade out where they are, and "Surface 1" flies
  from its card into its corner-pin quad inside the output canvas, where its handles become
  live once it has settled. The Straight canvas rectifies the surface the same way. The Board
  tab reverses it: the surface flies back to its card, the cards fade in and the view returns
  to the same Board pan/zoom as before.
- After 2: the view zooms onto the content card and the texture with the slice takes the
  card's place, the rest fading; the Board button returns to the Board with the content card
  selected.
- After 3: the same fold onto the image card: the view zooms onto the photo, the other cards
  fade. The header reads the image's name and kind. The Board button folds back to the Board,
  where the image card shows the photo.

## Step: Tracing surfaces on a photo

**Action:**
1. On the Board, right-click the reference image card and choose **Trace New Surface**.
2. Double-click the image card. Drag the four round corner handles of the new quad onto the
   corners of a wall in the photo. Press Ctrl+Z once, then Ctrl+Y.
3. Click the **Straight** button in the header, then **Photo**.
4. Click the **Board** button. Right-click the image card again and choose **Trace Surface 1
   Here** (any untraced surface).

**Expected:**
- After 1: a new surface "Surface N" appears in the SURFACES column, selected, and a green
  quad with its name is drawn over the middle of the image card on the Board — with live round
  corner handles, since the surface is selected (they also show while the image itself is
  selected, and go away when neither is).
- After 2: inside the image's space the quad has live corner handles; the first corner drag
  selects the surface; dropping a corner is one undo step. Ctrl+Z moves the corner back,
  Ctrl+Y forward. The header shows the surface's name beside the Photo / Straight buttons.
- After 3: the photo warps over about half a second so the traced quad becomes an upright
  rectangle with the surface's own aspect (Size in the Parameter window), drawn at full
  opacity inside a green frame while the rest of the photo dims and the photo's own frame
  and the label fade; the view settles on that rectangle. Photo eases it back and the
  handles return.
- After 4: back on the Board both traced quads sit on the image card in green; clicking a
  quad's name selects that surface without leaving the Board, and its corners can be dragged
  right there, one undo step per drag.

## Step: Straight on a traced surface stays on the photo

**Action:**
1. On the Board select "Surface 1" (traced and mapped to P1) and click the **Straight** tab.
2. With Straight still active, click the other traced surface's item in the SURFACES column,
   then "Surface 1" again.
3. Click the **Board** tab.

**Expected:**
- After 1: one continuous move of about half a second, entirely on the image card: the view
  zooms onto the traced quad, the photo warps in place until the quad is an upright rectangle
  with the surface's aspect, the rest of the photo dims and the label fades. The surface's
  own card elsewhere on the Board is not involved; no "Projector Camera" header appears
  (it is on the Calibrate tab only), so nothing shifts at the start.
- After 2: the scene turns from one rectified wall to the other, the warp and the view easing
  together rather than cutting, and back.
- After 3: the photo relaxes back to its unwarped state and the view returns to the Board.

## Step: Editing on the straightened photo

**Action:**
1. With "Surface 1" straightened (previous step), drag its right edge handle 100 px further
   right, release; press Ctrl+Z.
2. Drag its top-right corner handle up by 50 px, release.
3. Click **+ Line** in the header and drag along a mortar line of the brick wall; repeat for a
   vertical feature (a door frame); click **Straighten**.
4. Double-click one of the lines, type its real length in metres, **Set**, then **Apply lengths**.
5. Right-click the "Surface 1" item in the SURFACES column and choose **Add region**.
6. Click the **Board** tab.

**Expected:**
- After 1: the rectangle itself stays put; while dragging, the photo re-warps live so the
  wall's edge is pulled into the frame's edge, and nothing moves on release. Ctrl+Z restores
  the previous trace in one step.
- After 2: the same for a corner: dragging pulls the wall's corner into the frame's corner,
  live; the frame never re-centres.
- After 3: the lines are drawn in the alignment colours; Straighten refines the trace so both
  lines come out level and plumb, with the photo easing to the result.
- After 4: the surface's Size in the Parameter window changes so the measured line reads its
  real length.
- After 5: the view stays on the straightened photo; "Region 1" appears as a green rectangle
  inside the wall, selected, with round corner handles, square edge handles and its anchor
  crosshair; its item is nested under "Surface 1" in the outliner. Dragging a corner resizes it
  about the opposite corner, an edge crops it, dragging its name moves it — each snapping to
  the wall's edges and centre and undoing in one step. The same handles exist for a selected
  region on the Board's surface card. Clicking a region's name selects it without leaving
  Straight.
- After 6: on the Board the "Surface 1" card now shows the straightened crop of the photo as
  its backdrop; its metadata reads the new size. Dragging an edge handle of that card changes
  the metadata while dragging, and the green traced quad on the image card crops along with
  it.

## Step: Reference points on the straightened photo

**Action:**
1. With "Surface 1" straightened (previous step), click **+ Point** in the header, then click
   a distinct feature inside the wall (a light switch, a brick corner).
2. Repeat for three more features, so four points exist; then click **+ Point** and press the
   **+ Line** button instead of clicking on the photo.
3. Drag the second point's handle 40 px to the right; then hold Shift and drag it 40 px
   further right; release; press Ctrl+Z twice.
4. Right-click the fourth point's handle and choose **Delete**.
5. Click the **Board** tab; then double-click the "Reference 1" image card.

**Expected:**
- After 1: the header hint reads "click a feature you can find on the real wall" while armed;
  the click leaves a green crosshair with a "P1" chip at that spot and the tool disarms (the
  next click on the photo does nothing).
- After 2: the points are named P1 to P4 in order; pressing **+ Line** while **+ Point** is
  armed switches the hint to the line one; only one tool is armed at a time. **Straighten**
  stays disabled: points don't count as lines.
- After 3: the plain drag moves P2 by 40 px; the Shift drag moves it by only about 4 px (a
  tenth). Each Ctrl+Z undoes one drag.
- After 4: P4 disappears; P1 to P3 keep their names and positions.
- After 5: on the Board the "Surface 1" card shows the three crosshairs at the same places on
  its photo backdrop; scaling the card with Ctrl-drag on an edge keeps them on their
  features. Inside the "Reference 1" image the same three points sit on the green traced
  quad, at the features they were placed on.

## Step: Calibrating the pin by its reference points

**Action:**
1. With "Surface 1" carrying three reference points (previous step) and mapped onto "Projector 1",
   select "Surface 1", click the **Output** tab and click **Project photo** in the header.
2. Drag the P1 handle on the projector canvas 60 px to the right, release.
3. Drag the P2 handle 40 px down, release; press Ctrl+Z; press Ctrl+Y.
4. Drag the P3 handle 30 px left, release; then double-click P3.
5. Add two more points on the straightened photo (see the previous step), return to **Output**,
   drag both onto the canvas, then drag the fifth one 30 px further.
6. Drag the percent field right of **Project photo** from 5% to 15%.
7. Click **Project photo** again.

**Expected:**
- After 1: the surface's content stays in the composite; around each point a disc of the
  straightened photo (about a tenth of the canvas height across) is projected over it, with a
  dim white crosshair. On the canvas every point shows the same disc under a white handle with
  its "P1".."P3" chip — also a point whose projection falls outside the projector frame, which
  has no disc on the wall but keeps its disc on the canvas so you can see which feature it marks.
- After 2: while dragging, the whole quad shifts so the P1 disc follows the cursor; on release
  P1 turns green (activated) and stays exactly where it was dropped. P2 and P3 keep riding the
  pin and moved with it.
- After 3: with two activated points the quad turns and scales so both sit at their targets;
  P1 does not move. Ctrl+Z puts the quad back and P2 back to idle (white); Ctrl+Y restores both.
- After 4: with three activated points the quad shears; P1 and P2 stay put. The double-click
  turns P3 white again and the quad does not change.
- After 5: with four activated points the quad keystones and every activated target is hit
  exactly. With five, the header shows "points miss by up to N px" with N above zero, and the
  fifth point's disc no longer sits exactly on its crosshair.
- After 6: the discs grow to three times their radius on the canvas and on the wall alike. Where
  "Region 1" overlaps a disc, the disc draws over the region's content, on the wall as on the
  canvas.
- After 7: the discs and crosshairs leave the composite (unless "Show size raster" is on); the
  handles and their green/white state stay on the canvas and survive a save and reload.

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
