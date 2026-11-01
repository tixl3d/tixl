---
id: output-setup-patches
title: Output Setup — Patches (the direct pipe)
scope: output-window
tags: [projection-mapping]
added: 2026-09-05
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - Three windows are visible - the graph window, the Parameter window, and one output window with its Flow Outliner shown (it opens with a selected SendToOutput; otherwise the toolbar's "Output Setup" button at its right end).
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
- **No** child item appears and P1 gains no chevron: an output's full-canvas base patch is
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

## Step: Fill resets a dragged patch to the whole canvas

**Action:**
1. In **Output** mode with "P1" selected, drag one corner handle of the canvas a little way inward.
2. Right-click the label that appeared on the patch and choose **Fill P1**.
3. Press Ctrl+Z.
4. Press Ctrl+Y, then right-click the "P1" item in the OUTPUTS column.

**Expected:**
- After 2: the image fills the canvas again, the label and the chevron on "P1" disappear, and "P1" is selected.
- After 3: the dragged corner and the label come back in one undo step.
- After 4: the image fills the canvas again, and the menu of "P1" lists no **Fill** entry.

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
- P1 gains a chevron and lists exactly **one** item, "Patch 1": the new tile, a centred quarter
  of the canvas (visible and draggable on the output canvas, not lying exactly on the canvas
  border). The full-canvas patch feeding P1 stays folded into the P1 row — it still shows the
  image behind the tile, and its connection still lands on P1.
- Ctrl+Z removes the new patch and P1 loses its chevron again.

## Step: Patch geometry edits from the card

**Action:**
With "Patch 1" selected, in its row of the patch list set **Width** and **Height** to 960 × 540,
then **X** and **Y** to 480 × 270. Press Ctrl+Z four times.

**Expected:**
- After the size edit the image occupies the top-left quarter of P1's canvas; after the
  position edit it sits centred.
- Each Ctrl+Z reverts exactly one of the four field edits.

## Step: A second patch, added empty and then fed

**Action:**
1. Right-click "P1" → **Add Patch**.
2. Drag the "Slice 1" item under the SendToOutput content onto the new "Patch 2" item.

**Expected:**
- After 1: "Patch 2" appears dimmed under P1 with no connection; its card says nothing is
  routed yet.
- After 2: a connection from "Slice 1" reaches "Patch 2" in the outliner and the item is no
  longer dimmed. Ctrl+Z unfeeds it again.

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

## Step: Rotating a patch in quarter turns

**Action:**
1. In the row of "Patch 1" in the patch list, click the clockwise rotate icon left of the settings
   icon; click it twice more; then type `100` into the row's **Rotation** field.
2. Right-click "Patch 1" on the output canvas and choose **Rotate 90°** four times.
3. With P1 fed directly (no listed patches), right-click "P1" and choose **Rotate Content 90°**.
4. Rotate a patch to 90°, then **Split into 2×2**; then undo, and choose **Use on Surface** instead.

**Expected:**
- After 1: the **Rotation** field reads -90°, 180°, then 90°. Each click turns the whole patch
  clockwise around its centre, its outline and its picture together: **Width** × **Height** goes from
  960 × 540 to 540 × 960, back to 960 × 540, then 540 × 960 again, and at 540 × 960 **X** × **Y** reads
  210 × -210 (the patch pokes above the canvas top). The typed 100 snaps to 90° and changes nothing further. A
  small wedge on the patch's outline, on the canvas and on the Board card, points out of the
  edge the picture's top now faces; at 0° it is gone. Each change is one undo step.
- After 2: the patch and its picture step round a quarter per click; after the fourth they are
  back at 540 × 960, at the same place as before the clicks.
- After 3: the picture on the whole canvas turns, and a "Patch 1" row appears under P1 — a turned
  patch is shown on its own, since its rotation is state you need to be able to see and undo.
- After 4: every tile of the split shows its part of the picture turned the same way. Promoted
  to a surface instead, the picture on the wall does not change: the surface lies on its side,
  and its size is measured along the picture, not along the canvas.

## Step: Fitting a patch to an aspect ratio

**Action:**
1. Select "Patch 1", set the **Rotation** field of its row back to 0°, then set **Scale mode** to **Fit**.
2. Set **Aspect ratio** to 2.39 : 1.
3. Set **Scale** to 50%.
4. Select "P1" and set its **Resolution (px)** to 1080 × 1920; then press Ctrl+Z.
5. Select "Patch 1" again and set **Scale mode** back to **Stretch**.

**Expected:**
- After 1: the X, Y, Width and Height of its row turn into read-only text, and **Aspect ratio**
  (16 : 9) and **Scale** (100%) appear above the list. The card reads "1920 × 1080 px on the canvas, centred" and the patch covers
  the whole canvas. On the output canvas the patch has no corner or edge handles, and dragging its
  label does not move it.
- After 2: the card reads "1920 × 803 px on the canvas, centred": the patch spans the full width,
  with equal gaps above and below.
- After 3: the card reads "960 × 402 px on the canvas, centred", still centred on the canvas.
- After 4: the canvas turns portrait and the patch re-fits at once: the card reads "540 × 226 px on
  the canvas, centred". Ctrl+Z restores the 1920 × 1080 canvas and the patch returns to 960 × 402.
- After 5: the row's fields are editable again, reading about 480 × 339 and 960 × 402, the handles
  return, and the patch stays exactly where it was. Each of the five changes is one undo step.

## Step: Use on Surface

**Action:**
Right-click "Patch 1" (in the panel or on its canvas label) → **Use on Surface**.

**Expected:**
- "Patch 1" disappears from P1 and a new surface item appears under SURFACES, selected,
  mapped to P1 and fed by "Slice 1".
- Nothing moves on the canvas: the surface's corner pin sits exactly where the patch
  was, now with the surface's anchor marker and its edge handles.
- The surface card shows Size (m) 1 × 0.625 (the tile's aspect).

## Step: A surface dropped on a patch that carries content replaces it

**Action:**
Drag the surface item created by "Use on Surface" onto the "Patch 2" item in the OUTPUTS column.

**Expected:**
- "Patch 2" disappears from P1 and the surface gains a second mapping onto P1, its corner pin
  exactly where "Patch 2" was. The patch fed content, so it hands it over rather than drawing
  the same pixels twice.

## Step: A surface dropped on an empty patch keeps it

**Action:**
1. Right-click the "P1" item → **Add Patch**, and leave the new patch unfed.
2. Drag any surface item onto that new patch's item.

**Expected:**
- After 2: the patch item stays under P1 and the surface is pinned to the patch's quad. An
  unfed patch is layout — a venue's pixel map is traced as patches that hold no content — so
  dropping a wall onto its area maps the wall there without consuming the area.
- The surface's Board card now shows the part of P1's canvas that its new mapping covers,
  at the Overlay percent. A surface whose name matches a patch shows that patch's area even
  with no mapping at all.

## Step: Straight needs a surface with a place to straighten against

**Action:**
1. Add a surface (the SURFACES column's **+**) and leave it unmapped and untraced.
2. With it selected, hover the **Straight** tab.

**Expected:**
- The tab is disabled and its tooltip asks for a surface that is mapped to an output or
  traced on a reference photo. Clicking it does nothing and the Board stays.

## Step: Clear Inputs on a patch

**Action:**
1. Right-click the patch that the surface was pinned to in "A surface dropped on an empty patch
   keeps it" and choose **Clear Inputs**.
2. Right-click that patch again and look at **Clear Inputs**.
3. Press Escape, drag "Slice 1" onto the patch's item, then right-click it → **Clear Inputs**.
4. Press Ctrl+Z.

**Expected:**
- After 1: the connection from the surface disappears. The surface item stays in SURFACES and the
  patch stays under P1 at the same place on the canvas.
- After 2: **Clear Inputs** is greyed out — nothing feeds the patch any more.
- After 3: the connection from "Slice 1" disappears and the patch item turns dimmed, but it stays
  under P1 at the same place on the canvas.
- After 4: the connection from "Slice 1" is back, in one undo step.

## Step: Clear Inputs on several selected items

**Action:**
1. Click the "Patch 1" item, then Ctrl+click the "Patch 2" item, so both are selected.
2. Right-click "Patch 2" and look at the menu, then choose **Clear Inputs**.
3. Right-click "Patch 2" again and look at **Clear Inputs**.
4. Press Escape to close the menu, then press Ctrl+Z.

**Expected:**
- After 2: **Clear Inputs** is the one enabled row among the patch actions, next to **Delete 2**;
  **Rotate 90°**, **Use on Surface**, **Duplicate** and **Rename** stay greyed out. Choosing it
  removes the connections into both patches, both items turn dimmed and stay under P1.
- After 3: **Clear Inputs** is greyed out — nothing feeds either patch any more.
- After 4: both connections are back, in one undo step.

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
- Every connection into the "P1" item itself disappears: the surface mappings onto it and content
  dropped directly on it. The patch items stay under "P1" with their connections, including those
  from surfaces pinned onto a patch, and the surfaces and their slices remain. The entry is greyed
  out when only patches are fed. Ctrl+Z restores everything in one step.
