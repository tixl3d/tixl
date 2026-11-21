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

The Output Window shows GPU buffers as points, with a sidebar for a table of their values. Buffers don't carry their element
type, so only 64-byte strides (the size of a point) can be shown as points.

## Step: Points with the sidebar hidden

**Action:**
Select the [RadialPoints] and look at the Output Window.

**Expected:**
- A segmented control floats at the top left: a closed eye (hidden sidebar, selected), a point icon with the
  point count in small text, and an info icon.
- The points are drawn as with a [VisualizePoints] with default settings, on top of the grid.
- Next to the control, the count, `64 bytes` and the buffer size.
- Dragging in the output orbits the camera; the toolbar shows the camera controls as for a rendered scene.

## Step: Table in the sidebar

**Action:**
Click the point icon.

**Expected:**
- A sidebar opens on the left with **Start** and **Count** fields and a table, one row per point, columns `Pos X` ... `F2`.
- The points move into the area right of the sidebar and below the toolbar, centered there; orbiting turns around its
  middle. Nothing is drawn behind the table.
- Dragging the splitter between sidebar and render resizes both.
- Hovering a row draws a blue ring around that point on the render.
- The list icon at the right of the control row maximizes the table over the whole view; clicking it again brings
  the render back.
- Zeros are drawn faintly, not black. The `Color.R` cell starts with a narrow strip in the point's color.
- While the radius grows, changing position values are tinted orange; while it shrinks, blue. Hovering a tinted value
  shows its previous value and the change in percent. With playback stopped, the tint disappears.
- The info icon shows the count, stride and size instead of the table.

## Step: Layout is shared between ops

**Action:**
With the table open, select a different point op (e.g. a [GridPoints]), then select the [RadialPoints] again.

**Expected:**
- Both ops show the sidebar with the table at the same width. Hiding it on one op also hides it on the other.

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
- Nothing is rendered: the sidebar covers the whole view, and the hidden-sidebar segment is grayed out.
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

