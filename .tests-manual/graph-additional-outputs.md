---
id: graph-additional-outputs
title: Additional Outputs on Operators
added: 2026-11-04
added-in-version: 4.3
scope: graph-window
tags: [graph, outputs, smoke]
prerequisites:
  - A project you can edit is open in the Graph Window.
related-help:
  - ../.help/docs/getting-started/Introduction.md
---

Operators with more than one output show the main output as a larger circle
under the node and every additional output as a smaller circle in the node's
bottom-right corner. The smaller circles replace the old triangle that only
opened a list of hidden outputs.

## Step: Additional outputs show as small circles

**Action:**
Add a `[RenderTarget]` (it has three outputs) and look at its bottom-right
corner. Hover the two small circles.

**Expected:**
- Two small circles sit side by side just inside the node's bottom-right corner,
  a constant gap apart, above the bottom border.
- The larger main output circle under the node and the node's right-edge circle
  (used for horizontal connections) are clearly larger than the extra outputs.
- Each small circle uses its output type's color, so the two can differ.
- Hovering a circle shows a tooltip with the output's name (`Depth Buffer` /
  `Normal Buffer`) and its type.

## Step: Dragging a circle connects that output

**Action:**
Drag one of the small circles onto the image input of another operator, e.g. a
`[Blur]`.

**Expected:**
- The connection preview starts at the circle, and dropping it creates the
  connection.
- The output now gets its own labelled row (`Depth Buffer`) whose anchor is a
  small circle too.
- Only the remaining hidden output keeps a circle in the bottom-right row.

## Step: Clicking a circle inserts a connected operator to the right

**Action:**
Click (without dragging) the remaining small circle.

**Expected:**
- The Symbol Browser opens to the right of the operator, at the row the
  additional output will take once it is connected.
- A connection line drops from the clicked small circle and turns into the
  middle of the placeholder's left edge, in the output type's color.
- No such line appears when inserting from the primary output.
- Pick `[Blur]`: it is inserted to the right and snaps horizontally to the
  operator, with its input row aligned to the new output row.
- The extra-output circles disappear for this operator once all additional
  outputs are connected.

## Step: Several additional outputs keep constant spacing

**Action:**
Add an `[AudioReaction]` and look at its bottom-right corner.

**Expected:**
- Three small circles are laid out in a row, extending left from the corner,
  with the same gap between every pair.
- Each circle responds to hover with its own output name and type.
- The circles are colored by their output types (e.g. the `Hit Count` circle
  differs from `Was Hit`).

## Step: Many additional outputs stay inside the node

**Action:**
Add a `[GetGeometryStats]` (many outputs) and zoom in.

**Expected:**
- The circle row grows left along the bottom edge, keeping constant spacing.
- All circles stay inside the node; none are drawn outside its outline.
- Zooming in makes the individual circles easier to target.

## Step: Undo restores the circle

**Action:**
Press `Ctrl+Z` after connecting an additional output.

**Expected:**
- The connection is removed and the output's small circle returns to the
  bottom-right row.
