---
id: output-setup-parameter-window
title: Output Setup — Properties in the Parameter Window
scope: output-window
tags: [projection-mapping]
added: 2026-09-04
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup is empty (a freshly created project works — it starts with "Setup 1" and no content, surfaces, or outputs).
  - Three windows are visible - the graph window, the Parameter window, and one output window with its Flow Outliner shown (it opens with a selected SendToOutput; otherwise the toolbar's list icon).
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
  **Show size raster** (off), **Anchor (-1..1)** (0 × -1, the bottom-centre).
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
  outliner is highlighted, and the outliner closes.

## Step: A selected SendToOutput shows parameters plus the setup side

**Action:**
With the SendToOutput op still selected in the graph, scroll to the bottom of the
Parameter window. Then drag the "SendToOutput" CONTENT item onto the "Surface 1" item
and re-select the op in the graph.

**Expected:**
- Before the drag: below the op parameters an **Output Setup** section shows
  **Resolution (px)** (read-only) and the line "0 slices, nothing shows them yet".
- After the drag: the line reads **"1 slice → 1 target"**.

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
Click `+` on **PROPS**, then `+` on **REFERENCE IMAGES**. Click the new prop item,
then the new reference image item.

**Expected:**
- For the prop: the output area shows a centered message naming it and pointing to
  the Parameter window; the Parameter window shows **Prop** with an editable
  **Height (m)** field (1.7).
- For the reference image: the Parameter window shows **Reference Image** with a
  Name field and the hint to drop a photo; the output area shows the (empty)
  reference canvas.
