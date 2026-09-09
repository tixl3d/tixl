---
id: output-setup-slice-space
title: Output Setup — Slices in the Source Space
scope: output-window
tags: [projection-mapping]
added: 2026-09-09
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one SendToOutput showing a recognisable image, "Surface 1" showing "Slice 1" and mapped to output "P1", and a patch "Patch 1" on P1 showing the same slice (the "Output Setup — Patches" set builds this).
  - The graph window and one output window with the Flow Outliner shown are visible.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the implicit slice (content piped onto a surface with no slice in sight), cutting a slice
by drawing on the source, and the two-way link between slices and what shows them: hover a slice
to light its consumers, hover a consumer to light its slice, and read the consumer list under
every slice label.

## Step: Content pipes onto a surface without a slice

**Action:**
1. Start from a source with no slices (delete every slice under "SendToOutput" in the CONTENT
   column). Drag the "SendToOutput" row onto "Surface 1", then onto "P1".
2. Look at the CONTENT column, the Board card and the Parameter window with the content row
   selected.
3. Double-click the "SendToOutput" card.

**Expected:**
- After 1: both connections work — the surface and a new patch show the content.
- After 2: no slice row appears under "SendToOutput" and it has no expander; the curves run
  from the content row itself to "Surface 1" and "Patch 1". The card shows the picture with no
  inner "Slice 1" frame. The op's Output Setup section reads "full frame → 2 targets".
- After 3: the source space shows the bare texture with no slice rect.

## Step: The implicit slice surfaces when it stops being the whole frame

**Action:**
1. On the Board, crop "Surface 1" by an edge (plain drag), then Ctrl+Z.
2. Right-click the "SendToOutput" row → **Add slice**, then Ctrl+Z.

**Expected:**
- After 1: "Slice 1" (cropped) and "Slice 2" (the full frame the patch keeps) appear as rows —
  the crop cloned the shared slice; undo folds them away again.
- After 2: "Slice 1" and "Slice 2" appear as rows, both full-frame; undo folds them away again.

## Step: Entering the source space

**Action:**
Double-click the "SendToOutput" card on the Board.

**Expected:**
- The view flies into the card: the source texture fills the canvas with "Slice 1" drawn as a
  rect over it. Under the slice's name label a small muted line reads "→ Surface 1, Patch 1".

## Step: Drawing a new slice

**Action:**
1. Press on an empty part of the texture (outside "Slice 1", or shrink it first) and drag a
   rectangle about a quarter of the texture wide; release.
2. Press on the texture and release without moving.
3. Press and drag a rectangle smaller than a few pixels; release.
4. Ctrl+Z.

**Expected:**
- After 1: while dragging, a magenta rect follows the cursor with its size in source pixels
  beside it, and its moving corner snaps to the texture's edges and midlines and to "Slice 1"'s
  edges with a guide line. On release a new slice "Slice 2" exists, selected, with the CONTENT
  column listing it under "SendToOutput" and the line under its label reading "unused".
- After 2: nothing is created; the selection is unchanged.
- After 3: nothing is created.
- After 4: "Slice 2" is gone.

## Step: Drawing clamps to the texture

**Action:**
Start a drag inside the texture and drag well past its right and bottom edges before releasing.

**Expected:**
- The draft rect stops at the texture's border; the created slice ends exactly there.

## Step: Hover links slices and consumers both ways

**Action:**
1. Hover the "Surface 1" row in the SURFACES column, then the "Patch 1" row in OUTPUTS.
2. Hover the "Slice 1" rect on the texture.
3. Select "Slice 1" (click its label) and hover its rect again.

**Expected:**
- After 1: "Slice 1"'s rect on the texture pulses while either row is hovered.
- After 2: the "Surface 1" and "Patch 1" rows in the outliner pulse.
- After 3: the same rows pulse for the selected (editable) slice; the consumer line under its
  label stays visible.

## Step: The consumer line follows the routing

**Action:**
Drag "Slice 2" (draw one if needed) from the CONTENT column onto the "P1" row, then look at the
texture; then delete the new patch and look again.

**Expected:**
- The line under "Slice 2" changes from "unused" to "→ Patch 2", then back to "unused".
