---
id: graph-layout-inputs
title: Layout Inputs of Selection
added: 2026-08-21
added-in-version: 4.3
scope: graph-window
tags: [user]
prerequisites:
  - A project you can edit is open in the Graph Window.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks `Layout Inputs` (`G`, also in the operator context menu). It tidies the
operators feeding the current selection one level at a time: the operator on the
main input is stacked on top of its target, operators on other inputs are snapped
to the left of their input row, and all of them join the selection. Pressing `G`
again grows the tree further up and left until nothing is left to arrange.

## Step: Stack a main-input source on top of its target

**Action:**
Create a `[Blur]` and a `[RadialGradient]`. Wire the gradient into the blur's
first (image) input, then drag the gradient somewhere below and to the right of the
blur so the cable is a visible curve. Select only `[Blur]` and press `G`.

**Expected:**
- `[RadialGradient]` jumps directly on top of `[Blur]` (same column, bottom edge
  touching the blur's top edge) and the cable becomes a snapped vertical
  connection.
- Both operators are now selected.
- `Ctrl+Z` moves the gradient back to where it was.

## Step: Snap a secondary-input source to the left of its row

**Action:**
Add a `[Value]` and wire it into the blur's second input (Size). Drag the
`[Value]` away so it is not snapped. Select only `[Blur]` and press `G`.

**Expected:**
- `[Value]` sits directly left of `[Blur]`, its top aligned with the Size input
  row, and the connection is drawn as a snapped horizontal link.
- `[RadialGradient]`, still snapped on top, stays where it is and is added to the
  selection.

## Step: Tall sources are not wedged between connected rows

**Action:**
Wire an operator with several visible input rows (e.g. `[Transform]`) into the
blur's Size input instead of the `[Value]`, and wire another `[Value]` into the
input row right below Size. Drag the tall operator away, select only `[Blur]`
and press `G`.

**Expected:**
- The tall operator is placed left of `[Blur]` with a visible gap (not snapped) at
  the height of the Size row, its cable drawn as a curve; the `[Value]` on the row
  below snaps into its row.

## Step: Tall sources snap when the covered rows are free

**Action:**
Disconnect the second `[Value]` again so the rows below Size are unconnected.
Drag the tall operator away, select only `[Blur]` and press `G`.

**Expected:**
- The tall operator now snaps directly left of the Size row; its lower rows simply
  sit next to the blur's unconnected rows (or hang below the blur).

## Step: Grow the tree with repeated presses

**Action:**
Wire another `[Value]` into the first `[Value]` (its Float input) and move it
somewhere far away. With only `[Blur]` selected, press `G` once, then again.

**Expected:**
- After the first press the blur's direct sources are arranged and selected; the
  far-away `[Value]` has not moved.
- After the second press the far-away `[Value]` is snapped to the left of the first
  `[Value]` and joins the selection.
- A third press changes nothing.

## Step: Shared outputs wait for all their consumers

**Action:**
Wire the `[RadialGradient]` output additionally into a new `[Blur]` placed
elsewhere. Drag the gradient away from both blurs so it is not snapped. Select
only the first `[Blur]` and press `G`. Then add the second `[Blur]` to the
selection and press `G` again.

**Expected:**
- After the first press the gradient does not move and is not added to the
  selection; only the `[Value]` on the Size input is arranged.
- After the second press the gradient sits one column left of the leftmost of the
  two blurs, aligned with the topmost blur's image row, with a small gap (it is
  not snapped since it feeds both) and both cables are curves. It is now selected.

## Step: Unrelated operators do not block a snap

**Action:**
Unsnap the gradient from the blur again (remove the second blur first if needed)
and drop any unrelated operator so that it partly covers the area directly left of
the blur's Size row. Select only `[Blur]` and press `G`.

**Expected:**
- `[Value]` snaps into the Size row anyway, overlapping the unrelated operator.
  Only operators taking part in the layout (the selection and the sources being
  arranged) are avoided.

## Step: Stacks hanging off kept sources are not covered

**Action:**
Build `[Blend]` with `[ColorGrade]` snapped left of its ImageA row and a
`[RenderTarget]` snapped left of the color grade (so the render target is attached
to the tree only through the color grade). Wire a two-row operator into the
blend's ImageB input, drag it away, select only `[Blend]` and press `G`.

**Expected:**
- The two-row operator is placed below the render target's rows with a gap, not on
  top of them; its cable curves up to the ImageB row.

## Step: Selected sources inside the selection are arranged too

**Action:**
With `[RadialGradient]` wired into the blur's image input but dragged away from it,
box-select both operators (so both are selected) and press `G`.

**Expected:**
- `[RadialGradient]` stacks on top of `[Blur]` — being selected does not pin it,
  because it feeds another selected operator.
- Selecting only the gradient (not the blur) and pressing `G` moves nothing; a
  selected operator without a selected consumer stays where it is.

## Step: Sections are not stretched after arranged operators

**Action:**
Put `[RadialGradient]` into a section (frame) somewhere far below `[Blur]`, which
stays outside the section. Select only `[Blur]` and press `G`.

**Expected:**
- The gradient stacks on top of the blur and leaves the section; the section keeps
  its size and position instead of growing to reach the gradient.
- `Ctrl+Z` moves the gradient back into the section.
