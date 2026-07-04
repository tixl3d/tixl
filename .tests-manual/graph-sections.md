---
id: graph-sections
title: Graph Sections (Grouping & Ownership)
added: 2026-07-04
added-in-version: 4.3
scope: graph-window
tags: [user, essential]
prerequisites:
  - A project you can edit is open with several operators on the graph.
  - The Graph Window is visible.
related-help:
  - ../.help/references/topics/ui/Annotations.md
---

Sections (formerly annotations) are colored frames that group operators. Since
4.3 an operator *belongs* to the innermost section that fully contains it, and
this ownership follows the geometry: moving, resizing, or deleting frames
updates the grouping automatically. This set walks through the interactions
that depend on it.

## Step: Create a section around a selection

**Action:**
Select two or three operators and press `Shift+S` (the old `Shift+A` should
work too). Type a title and press `Enter`.

**Expected:**
- A frame appears around the selected operators with some margin.
- The title shows in the frame header.
- `Ctrl+Z` removes the frame again without moving the operators.

## Step: Create a section from the Edit menu

**Action:**
Clear the selection (`Esc` or click empty canvas). Open the app menu →
`Edit` → `Add Section`.

**Expected:**
- An empty frame appears near the center of the graph view.
- The menu entry shows the keyboard shortcut.

## Step: Move a section with its content

**Action:**
Drag a section by its header to a different place on the canvas.

**Expected:**
- The operators inside move together with the frame.
- Operators outside the frame stay put.
- A single `Ctrl+Z` returns frame and operators to where they were.

## Step: Move a section without its content

**Action:**
Hold `Ctrl` and drag a section header away from its operators, dropping it on
empty canvas. Then drag the empty frame back over a few loose operators and
release.

**Expected:**
- With `Ctrl` held, only the frame moves; the operators stay behind.
- After dropping the frame over loose operators, dragging the frame normally
  (without `Ctrl`) now takes those operators along — they were adopted.
- The operators left behind at the old location no longer move with the frame.

## Step: Resize adopts and releases operators

**Action:**
Drag the resize corner of a section so it grows to fully enclose a nearby
operator. Then resize it back so the operator is outside again.

**Expected:**
- After growing, dragging the section moves the newly enclosed operator along.
- After shrinking, the operator is released and stays put when the section
  is moved.
- An operator only half inside the frame does not get adopted.

## Step: Resize from any edge or corner

**Action:**
On a reasonably large section, hover each of the four edges and the three free
corners (top-right, bottom-left, bottom-right — top-left is the collapse
chevron) and drag each one.

**Expected:**
- The cursor changes to the matching resize arrows on hover.
- Each edge/corner resizes the frame from that side; the opposite side stays
  anchored.
- Edges snap to neighboring section borders and the grid.
- The frame can't be resized smaller than roughly one operator cell.
- On a very small (or far zoomed-out) frame only the bottom-right corner
  offers resizing, so the frame stays easy to grab and drag.

## Step: Collapse toggle scales with zoom

**Action:**
Zoom in and out on a section with a label and watch the chevron toggle in the
header, clicking it at different zoom levels.

**Expected:**
- The chevron scales with the title text when zooming, instead of staying a
  fixed tiny size.
- It stays comfortably clickable even when zoomed far out.
- The label text sits next to the chevron without overlapping it.

## Step: No accidental header grabs when zoomed in

**Action:**
Zoom in until one section fills most of the graph view (only an edge or two
visible). Try to fence-select operators by dragging from empty space near the
section's top edge.

**Expected:**
- The drag starts a fence selection — the section is not grabbed or moved.
- After zooming out so the section covers clearly less than the full view,
  dragging its header moves it again as usual.

## Step: Drag operators in and out

**Action:**
Drag a loose operator fully into a section frame and release. Then drag it
back out onto empty canvas.

**Expected:**
- After dropping inside, moving the section takes the operator along.
- After dragging it out, the operator no longer follows the section.
- `Ctrl+Z` after each drag restores both the position and the grouping.

## Step: Collapse and expand

**Action:**
Click the chevron in a section header to collapse it. Move the collapsed
section, then expand it again.

**Expected:**
- Collapsing folds the frame to its header; the operators inside disappear.
- Connections into the hidden operators are routed to the collapsed frame.
- Moving the collapsed frame and expanding shows all operators again, with
  their layout intact relative to the frame.
- Dropping a loose operator onto the area below a collapsed header does
  *not* make it vanish — collapsed sections don't adopt.

## Step: Nested sections

**Action:**
Create a small section inside a larger one (select an operator inside the big
frame, press `Shift+S`). Drag the outer section by its header.

**Expected:**
- The inner section and all operators move together with the outer frame.
- Dragging only the inner section leaves the outer frame in place.
- Collapsing the outer section hides the inner frame and its operators too;
  expanding brings both back. The inner frame's own collapse state is
  preserved through the round-trip.

## Step: Old project loads correctly

**Action:**
Open a project last saved with an older TiXL version that contains
annotations, including one collapsed annotation if available.

**Expected:**
- All frames appear exactly as before, with titles and colors preserved.
- Collapsed frames are still collapsed and expand correctly.
- Dragging a frame takes the operators it visually contains along.

## Step: Legacy graph stays consistent

**Action:**
Switch the graph to the legacy view (if available in your build), drag an
operator out of a section there, then switch back to the standard graph view
and drag the section.

**Expected:**
- The legacy view renders sections and moves them with their content.
- After switching back, the operator moved out in the legacy view no longer
  travels with the section — ownership synced up automatically.
