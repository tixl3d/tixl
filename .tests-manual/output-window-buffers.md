---
id: output-window-buffers
title: Point Buffers in the Output Window
scope: output-window
tags: [points, output-window]
added: 2026-11-17
added-in-version: 4.4
prerequisites:
  - A project with a [RadialPoints] whose Radius is animated or driven by time (e.g. via [AnimValue]).
  - Playback is running.
related-help:
  - ../.help/docs/getting-started/Introduction.md
---

The Output Window shows GPU buffers as points or as a table of their values. Buffers don't carry their element
type, so only 64-byte strides (the size of a point) can be shown as points.

## Step: Points mode

**Action:**
Select the [RadialPoints] and look at the Output Window.

**Expected:**
- A segmented button offers **Points** and **Table**, with **Points** selected.
- The points are drawn as with a [VisualizePoints] with default settings, on top of the grid.
- The row next to the button lists the count, `64 bytes` and the buffer size.
- Dragging in the output orbits the camera; the toolbar shows the camera controls as for a rendered scene.

## Step: Table mode

**Action:**
Click **Table**.

**Expected:**
- **Start** and **Count** fields appear, and a table lists one row per point with columns `Pos X` ... `F2`.
- Zeros are drawn faintly, not black.
- The `Color.R` cell starts with a narrow strip in the point's color.
- While the radius grows, changing position values are tinted orange; while it shrinks, blue. Larger changes are tinted more strongly.
- Hovering a tinted value shows its previous value and the change in percent.
- With playback stopped, the tint disappears.

## Step: Mode is shared between ops

**Action:**
With **Table** selected, select a different point op (e.g. a [GridPoints]), then select the [RadialPoints] again.

**Expected:**
- Both ops are shown as a table. Switching back to **Points** on one op also shows the other as points.

## Step: Start and Count

**Action:**
Set **Start** to 10 and **Count** to 5.

**Expected:**
- The table lists exactly the points 10 to 14, with their indices in the first column.
- Start can't be dragged past the last point.

## Step: Buffers with other strides

**Action:**
Select an op that outputs a buffer with another stride (e.g. a particle simulation's or a custom compute shader's buffer).

**Expected:**
- **Points** is grayed out; its tooltip explains that only 64-byte strides can be shown as points.
- The table shows one column per float, labelled `[0]`, `[1]`, ...

## Step: Connection tooltip

**Action:**
Hover the connection leaving the [RadialPoints] in the graph.

**Expected:**
- The tooltip shows only the buffer's count, stride and size, no controls or table.

## Step: ReadPointsFromGpu zeros

**Action:**
Connect the points to a [ReadPointsFromGpu] and select it.

**Expected:**
- Zero values in its list are drawn faintly instead of black.
- With Update Continuously on and the radius animated, changing values are tinted orange or blue as in the Output Window table.
- The first `Color` field of each row shows a narrow strip in that color.

