---
id: output-setup-region-slices
title: Output Setup — Slices from Regions
scope: output-window
tags: [projection-mapping]
added: 2026-11-01
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one surface "Wall", 3×2 m, mapped to an output.
  - The Graph window, the Parameter window and one output window in setup mode with its Flow Outliner shown are visible.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers **Create Slices from Regions**: one picture the size of a surface, cut into a slice per region from where
each region sits on the surface.

## Step: One slice per region, in place

**Action:**
1. Right-click "Wall" → **Add region** twice, and rename the regions "TV 1" and "TV 2".
2. In the Straight view, place "TV 1" over the left half of "Wall" and "TV 2" over the right half, both full height.
3. Right-click "Wall" in the outliner and choose **Create Slices from Regions**.

**Expected:**
- A SendToOutput op named "Wall" is added to the graph and appears in CONTENT, selected, with slices "TV 1" and
  "TV 2" under it. Its Resolution has a 3 : 2 aspect.
- On the Board, its card has the same 3 : 2 shape and shows that Resolution, although nothing is connected to
  the op yet. The slice "TV 1" covers the left half and "TV 2" the right half.
- SURFACES shows a connection from each slice to its region. A picture crossing the middle of the op's output
  continues without a jump from "TV 1" to "TV 2" on the output.

## Step: Undo takes back op and slices together

**Action:**
Press Ctrl+Z once.

**Expected:**
- The op, its CONTENT item, both slices and their connections are gone. Ctrl+Y restores them.

## Step: Updating keeps the routes

**Action:**
1. Route the slice "TV 1" to the output as well, by dragging it onto a patch.
2. Shrink "TV 2" to half its width, and add a third region "TV 3" in the free area.
3. Right-click "Wall" and choose **Update Slices from Regions**.

**Expected:**
- No second op is added. "TV 2" now covers only its new area on the card, and "TV 3" gets a new slice.
- The route from "TV 1" to the patch is still there, so that slice now has two consumers and "TV 1" gets a new
  slice of its own while the patch keeps the shared one.

## Step: Updating follows names and order

**Action:**
1. Rename the region "TV 1" to "Left screen". In the outliner, drag "TV 3" above "TV 2" under "Wall".
2. Right-click "Wall" and choose **Update Slices from Regions**.
3. Press Ctrl+Z.

**Expected:**
- After 2: the slice shown on "Left screen" is now also called "Left screen", and the slices under the content
  item are listed in the same order as the regions under "Wall". A name typed on a slice by hand is overwritten.
- After 3: the old names and order return in one step.

## Step: Re-cutting selected content

**Action:**
Add another SendToOutput op. Select it in CONTENT, Ctrl+click "Wall", right-click "Wall" and choose
**Create Slices from Regions on \<name\>**.

**Expected:**
- All three regions now show new slices of the selected content, and its Resolution is set to the surface's aspect.

## Step: Regions with their own pin project without their parent

**Action:**
1. Build [StageGeometry] → [GeometryToMesh] → [ProjectOntoStage], with an image on its Image input and its Viewer
   Position in front of "Wall", and view its output.
2. Right-click "TV 1" and "TV 2" and give each a place of its own on the output, by dragging it onto a patch.
3. Clear "Wall"'s own route to the output, so only the regions are on it.

**Expected:**
- After 3: the output still shows the projected picture in the two regions' places, and no status warning shows on
  [ProjectOntoStage]. It stays empty, with a warning, only when neither "Wall" nor any region has a place.

## Step: Projecting into a slice layout

**Action:**
1. Keep the graph from the previous step, with "Wall" not on the output. Connect [ProjectOntoStage] to the "Wall"
   SendToOutput that **Create Slices from Regions** made.
2. Set [StageGeometry]'s **Canvas** to "Wall".

**Expected:**
- Before 2: the regions show mismatched parts of the picture, as the canvas is laid out by the output's patches.
- After 2: the send's card shows one continuous picture of the whole wall, the regions show their parts of it, and
  the picture continues across them. Nothing shows on the output outside the regions' patches.

## Step: Offered only where it applies

**Action:**
Right-click a surface without regions, then right-click the region "TV 1".

**Expected:**
- Neither menu offers the entry.
