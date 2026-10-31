---
id: output-setup-pixel-map
title: Output Setup — Pixel Map and Patch Table
scope: output-window
tags: [projection-mapping]
added: 2026-09-14
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - Three windows are visible - the graph window, the Parameter window, and one output window with its Flow Outliner shown (it opens with a selected SendToOutput; otherwise the toolbar's "Output Setup" button at its right end).
  - A LoadImage op showing a recognisable image exists in the graph, and a second image file is available in the project's assets (for example `chipmunk.jpg` from the Examples package).
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the output's **Pixel Map** (a venue layout image drawn over the output canvas) and the
**Patches** table in the Parameter window, where patch rects are typed in from a spec sheet.

## Step: Build the test setup

**Action:**
1. Connect the LoadImage op to a new **SendToOutput** op.
2. Click `+` on **OUTPUTS**, then select the new **"P1"** item.
3. In the Parameter window, set **Resolution (px)** to `2000` × `1000`.

**Expected:**
- The Parameter window shows the P1 card with an empty **Reference Layout** field below a **Patches** section
  that has only an **Add** button.

## Step: Picking a pixel map creates a reference image

**Action:**
1. In the **Reference Layout** field, type the second image's name and pick it.
2. Switch the mode segmented button to **Output**.

**Expected:**
- After 1: an **Opacity** field appears at 0.5, and a size line reads "… px · resolution is
  2000×1000" in the attention colour with a **Use as Resolution** button next to it
  (the image's size differs from 2000 × 1000).
- After 2: the picked image is drawn over the whole P1 canvas at half strength; the Board
  gains a card named "P1 map".

## Step: Opacity and the canvas-size button

**Action:**
1. Drag **Opacity** to `1`, then press Ctrl+Z.
2. Click **Use as Resolution**.

**Expected:**
- After 1: the image fully covers the canvas, then returns to half strength.
- After 2: **Resolution (px)** shows the image's own pixel size and the size line reads
  "… px · matches the resolution" in muted text with no button.

## Step: Typing a patch from the table

**Action:**
1. Drag the "SendToOutput" CONTENT item onto the "P1" item.
2. Click **Add** in the Patches header.
3. In the Patches header, set **in** to **Pixels** and type `100` into X, `50` into Y, `400`
   into Width and `300` into Height of the new patch's row.

**Expected:**
- After 2: the Parameter window switches to the new patch's card; its table lists two
  rows ("Patch 1" and "Patch 2"), the second row highlighted.
- After 3: the output canvas shows the patch as a 400 × 300 rectangle whose top-left corner
  sits 100 px right and 50 px down from the canvas corner, over the pixel map.

## Step: Rotation and selection from the table

**Action:**
1. Type `90` into the **Rotation** cell of the same row.
2. Click the name cell of "Patch 1".
3. Press Ctrl+Z twice.

**Expected:**
- After 1: the patch's picture turns a quarter turn on the canvas, the rect unchanged.
- After 2: the Parameter window shows "Patch 1"'s card, the first table row highlighted.
- After 3: the turn is undone, then the rect edit; each is one undo step.

## Step: Default name, default resolution and the implicit patch

**Action:**
1. Drag "Local / Display 1" from LOCAL BINDINGS onto empty space in OUTPUTS to create an output, and
   select the new item.
2. Type `Front` into its **Name** field and press Enter; then clear the field and press Enter.
3. Set **Resolution (px)** to `1920` × `1080`, then click the revert icon at the end of that row.
4. Drag a CONTENT item onto the output, click **Add** in the Patches header, and look at the list.

**Expected:**
- After 1: **Name** is empty and shows the display's name as gray placeholder text; the item in the
  outliner carries that same name. **Resolution (px)** reads the display's size, faded, with no revert icon.
- After 2: the outliner item reads "Front", and returns to the display's name once the field is cleared.
- After 3: the revert icon appears with the typed size; the click returns the row to the display's size, faded again,
  and Ctrl+Z brings 1920 × 1080 back.
- After 4: the list's first row is called "Full Canvas" and the second "Patch 1", the same "Patch 1" the
  outliner lists. Rows are 2 px apart, and "Patches" and **Add** are inset from the rows' edges.

## Step: Patches keep their names when reordered

**Action:**
1. With the list from the step above, click **Add** once more, so it reads "Full Canvas", "Patch 1", "Patch 2".
2. Drag the grip of "Patch 2" upward past "Patch 1" and keep holding; move further down and up again; release.
3. Press Ctrl+Z.

**Expected:**
- After 2: the row keeps its name and its values while it moves, stays under the cursor, and the outliner
  order follows.
- After 3: the whole drag reverts in one step.

## Step: Removing the pixel map

**Action:**
1. Select "P1" in the outliner.
2. Click the small close icon next to the **Reference Layout** field.

**Expected:**
- The **Reference Layout** field is empty again, the canvas no longer shows the image, and
  the "P1 map" card is still on the Board (the image stays in the setup).
