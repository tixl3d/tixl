---
id: output-setup-slice-space
title: Output Setup — Slices on the Content Card
scope: output-window
tags: [projection-mapping]
added: 2026-09-09
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one SendToOutput showing a recognisable image, "Surface 1" showing "Slice 1" and mapped to output "P1", and a patch on P1 showing the same slice (the "Output Setup — Patches" set builds this).
  - The graph window and one output window with the Flow Outliner shown are visible.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers slices where they live: on the send's card on the Board, whose thumbnail is the source
seen flat. Also the implicit slice (content piped onto a surface with no slice in sight) and the
two-way link between a slice and what shows it.

## Step: Content pipes onto a surface without a slice

**Action:**
1. Start from a send with no slices (delete every slice under "SendToOutput" in the CONTENT
   column). Drag the "SendToOutput" row onto "Surface 1", then onto "P1".
2. Look at the CONTENT column, the send's Board card, and the Parameter window with the content
   row selected.

**Expected:**
- After 1: both connections work — the surface and the output show the content.
- After 2: no slice row appears under "SendToOutput" and it has no expander; the curves run
  from the content row itself. The card shows the picture with no inner "Slice 1" frame. The
  op's Output Setup section reads "full frame → 2 targets".

## Step: The slice appears once it stops being the whole frame

**Action:**
1. Right-click the "SendToOutput" row → **Add slice**, then Ctrl+Z.
2. Right-click the send's **card** on the Board → **Add slice**.

**Expected:**
- After 1: "Slice 1" and "Slice 2" appear as rows, both full-frame; the undo folds them away.
- After 2: the same, reached from the card — the card's menu is the row's menu.

## Step: Editing a slice on the card

**Action:**
1. Click "Slice 1" in the CONTENT column, then look at the send's card.
2. Drag the slice's right edge to the left, then a corner, then its name label.
3. Ctrl+Z three times.

**Expected:**
- After 1: the slice is drawn on the card with handles; the Parameter window shows its Position
  and Size in source pixels.
- After 2: the edge crops it, the corner scales it with its aspect held, the label moves it.
  Each snaps to the source's borders and midlines and to the other slice, drawing a guide line.
- After 3: each edit is reverted separately.

## Step: Duplicating a slice

**Action:**
Right-click the "Slice 1" label on the card (or its row) → **Duplicate**.

**Expected:**
- A second slice with the same rect appears, selected, listed under the send.

## Step: A slice says where it goes

**Action:**
1. Look under each slice's name label on the card.
2. Hover the "Surface 1" row in SURFACES, then the patch row under P1.
3. Hover a slice's rect on the card.

**Expected:**
- After 1: a small muted line reads "→ Surface 1, Patch 1" under the slice they show, and
  "unused" under one nothing shows.
- After 2: that slice's rect on the card pulses while either row is hovered.
- After 3: the rows of everything showing it pulse in the outliner.
