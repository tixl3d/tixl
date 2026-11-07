---
id: output-setup-parameter-window
title: Output Setup — Properties in the Parameter Window
scope: output-window
tags: [projection-mapping]
added: 2026-09-04
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - Three windows are visible - the graph window, the Parameter window, and one output window with its Flow Outliner shown (it opens with a selected SendToOutput; otherwise the toolbar's "Output Setup" button at its right end).
---

Covers Phase A of the UI restructuring: entity properties moved from the setup outliner's
footer card into the Parameter window. The last pick — graph op or setup entity — owns
the window, and picking one deselects the other, so there is never more than one selected
thing. The first step builds the entities every later step
refers to, so names match exactly.

## Step: Build the test setup

**Action:**
In the Flow Outliner, click the `+` at the right of each column header, in this order:
1. `+` on **CONTENT**
2. `+` on **SURFACES**
3. `+` on **OUTPUTS**

**Expected:**
- After 1: a **SendToOutput** op appears centered in the graph, and a "SendToOutput"
  item appears under CONTENT.
- After 2: a item **"Surface 1"** appears under SURFACES, selected.
- After 3: a item **"P1"** appears under OUTPUTS, selected.

## Step: Selecting an entity fills the Parameter window

**Action:**
Click the "Surface 1" item.

**Expected:**
- The Parameter window shows a card headed by a grid icon + **Surface**, then an
  editable **Name** field reading "Surface 1".
- Below: **Render** checkbox (on), **Position (m)** (3 fields, all 0),
  **Size (m)** (1 × 1) with the lock-aspect and measure icons,
  **Show size raster** (off), **Show measuring lines** (off), **Anchor (-1..1)** (0 × -1, the bottom-centre).
- The outliner itself shows **no** properties card anymore.

## Step: Picking takes the window and deselects the other side

**Action:**
1. Click the SendToOutput op in the graph window.
2. Click the "Surface 1" item in the outliner.
3. Click the SendToOutput op in the graph again.
4. Click the "Surface 1" item again, then click the empty graph background.

**Expected:**
- After 1: the Parameter window shows the op's parameters (Texture, Update, Color), and
  the "SendToOutput" CONTENT item is highlighted in the outliner.
- After 2: it switches to the Surface card, and the op loses its selection outline in
  the graph. The CONTENT item is no longer highlighted; only "Surface 1" is.
- After 3: it switches back to the op's parameters; "Surface 1" is no longer
  highlighted, the CONTENT item is.
- After 4: the Parameter window shows the composition (no entity card), no item in the
  outliner is highlighted, and the outliner stays open.

## Step: Working in the graph keeps the setup open

**Action:**
1. Pin the output window to any op other than a SendToOutput, using the pin in its toolbar. Then open the
   setup with the toolbar's "Output Setup" button.
2. Click a different op in the graph window, one that is not a SendToOutput.
3. Click the empty graph background.
4. With that op selected again, press and hold `P` for a second, then release it.
5. Press `P` briefly.

**Expected:**
- After 2 and 3: the output window keeps showing the Board and the Flow Outliner. The Parameter window
  follows the graph selection.
- During 4: the output window shows the selected op's output. After the release it is back on the Board with
  the Flow Outliner.
- After 5: the output window shows the selected op's output and stays there, with the toolbar's "Output
  Setup" button back in its usual place.

## Step: A selected SendToOutput shows parameters plus the setup side

**Action:**
With the SendToOutput op still selected in the graph, scroll to the bottom of the
Parameter window. Then drag the "SendToOutput" CONTENT item onto the "Surface 1" item
and re-select the op in the graph.

**Expected:**
- Before the drag: below the op parameters an **Output Setup** section shows an editable
  **Resolution (px)** at 0 × 0, a line saying what it currently renders at, and
  "full frame, nothing shows it yet".
- After the drag: that last line reads **"full frame → 1 target"**, and the rendering line now
  reports the bound output's canvas size — 0 × 0 means "follow the output".
- Typing 1280 × 720 into the field pins it there; clearing both back to 0 returns to following
  the output.

## Step: Renaming through the Name field

**Action:**
Click the "Surface 1" item. In the card's **Name** field, replace the text with
"Wall Left" and press Tab.

**Expected:**
- The SURFACES item updates to "Wall Left" when the field loses focus (not per
  keystroke).
- One Ctrl+Z restores "Surface 1" in both the card and the item.

## Step: Field edits stay single undo steps

**Action:**
With the surface selected, drag the **Size (m)** X field from 1 up to about 3 in one
uninterrupted drag, then press Ctrl+Z once.

**Expected:**
- During the drag the surface's Board card grows continuously while its corner-pin quad on the
  Output tab and its trace on the photo stay put — the Size field declares how big the wall is,
  it does not stretch the projection; a measuring line or region on the surface scales with it.
- The single undo restores X to exactly 1; a second undo affects the *previous*
  edit (the rename, if it was redone — otherwise nothing surface-related), never a
  fragment of the drag.

## Step: Kinds without a canvas point to the Parameter window

**Action:**
Right-click empty Board and choose **Add Person** → **Man Pointing**, then again and choose **Add Reference
Image**. Click the new person card, then the new image card.

**Expected:**
- For the person: the output area shows a centered message naming it and pointing to
  the Parameter window; the Parameter window shows **Person** with an editable
  **Height (m)** field (1.8) and an **Opacity** field (0.3).
- For the reference image: the Parameter window shows **Reference Image** with a
  Name field and the hint to drop a photo; the output area shows the (empty)
  reference canvas.

## Step: A person's opacity

**Action:**
1. Click the person card on the Board, then drag its **Opacity** field in the Parameter window up to 1.
2. Press Ctrl+Z.
3. Set **Opacity** to 0, then click an empty spot of the Board.
4. Click where the figure stands, set **Opacity** to 0.8, save the project and reopen it.

**Expected:**
- After 1: the green silhouette gets stronger while dragging, up to fully solid.
- After 2: it returns to 0.3 in one undo step.
- After 3: the figure is invisible, with only its height label left above where it stands.
- After 4: the click selects the invisible figure, which shows its white frame; after reopening, the figure is
  still at 0.8.

## Step: The size warning respects a set Resolution

**Action:**
1. Route one SendToOutput with two slices to two outputs of different sizes (1920×1080 and 2560×1440), with the
   op's **Resolution** at 0×0. Click the op in the graph window.
2. Set the op's **Resolution** to 3840×1080.
3. Set **Resolution** back to 0×0.

**Expected:**
- After 1: the op shows the "Routed to outputs of different sizes" warning on hover, and the Parameter window shows
  a magenta "also routed to an output of..." line under **Rendered (px)**.
- After 2: the warning and the magenta line disappear, and **Rendered (px)** reads 3840×1080.
- After 3: the warning and the magenta line come back.

## Step: Raster and measuring lines are separate switches

**Action:**
1. Select a surface that is mapped onto an output and has at least one measuring line. Tick **Show size raster** only.
2. Untick it and tick **Show measuring lines** only.
3. Tick both, save, and open the setup in an exported Player build.

**Expected:**
- After 1: the output shows the metre raster over the surface, without the measuring lines.
- After 2: the output shows the measuring lines, without the raster.
- After 3: the editor shows both; the Player shows neither the raster nor the lines.
