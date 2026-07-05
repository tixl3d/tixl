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

## Step: Expanding pushes neighbors aside

**Action:**
Collapse a section that has content, then move another section (or a few loose
operators) into the space right below its header. Expand the collapsed section
again via its chevron.

**Expected:**
- Expanding pushes the frames below downward so nothing overlaps the revealed
  area; their internal arrangement is preserved. Loose operators are never
  pushed — one sitting in the revealed area simply becomes a member of the
  expanded section.
- A single `Ctrl+Z` collapses the section again *and* returns the pushed
  neighbors to their previous places.
- Collapsing never pulls neighbors back in on its own.
- If a *collapsed* frame gets pushed down across an open section, its hidden
  operators stay hidden and stay owned by it — they don't pop up inside the
  open section. Expanding the pushed frame later reveals them where expected.

## Step: Inserting into a stack grows the section

**Action:**
Inside a section, build a vertical stack of snapped operators that ends close
to the frame's bottom border. Try each of these near the bottom border:
drag another operator into the middle of the stack; drop a connection onto a
multi-input so a new input row appears; insert a new operator into the stack
via the symbol browser (`Tab`).

**Expected:**
- Whenever the stack gets pushed down past the border, the section grows so
  the operators stay inside it.
- If another frame sits right below, it gets pushed down as well.
- One `Ctrl+Z` reverts the triggering edit, the growth, and the pushes
  together.
- Dragging an operator *out* of the section does not grow the frame.

## Step: Pasting near the border grows the section

**Action:**
Copy a couple of operators (`Ctrl+C`). Place the mouse inside a section close
to its bottom-right corner and paste (`Ctrl+V`). Repeat with `Ctrl+D`
duplicate of ops inside the section near its border.

**Expected:**
- If the pasted/duplicated operators would stick out past the bottom or right
  border, the section grows to include them (with a small margin).
- Frames below/right of the grown section get pushed away instead of being
  overlapped.
- One `Ctrl+Z` reverts paste, growth, and pushes together.
- Pasting with the mouse *outside* any section never resizes one.

## Step: Slow-drag against the border grows the frame

**Action:**
Drag an operator inside a section toward the frame's bottom or right border.
First push *slowly* against and across the border; then repeat the same move
fast. Also try the slow push while holding `Shift`.

**Expected:**
- Moving slowly, the border yields: the frame grows and keeps the operator
  inside, and frames beyond the border get pushed away on release.
- Moving fast, the operator passes through the border and leaves the section.
- With `Shift` held, the border never yields regardless of speed.
- One `Ctrl+Z` reverts move, growth, and pushes together.

## Step: Resizing never moves anything else

**Action:**
Place another frame and a few loose operators close below and right of a
section. Resize the section's borders over and past them, then back.

**Expected:**
- No other frame or operator changes its position, ever — resizing only
  changes this one frame's rect.
- Operators and frames that end up mostly inside get adopted/nested (moving
  the frame afterwards takes them along); resizing back releases them.
- One `Ctrl+Z` reverts just the resize.

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

## Step: Expanding a nested section grows its parent

**Action:**
Inside a larger section, collapse one of two nested frames and move it just
above its expanded sibling. Expand it again via its chevron.

**Expected:**
- The sibling frame below gets pushed down.
- The outer section grows so both nested frames stay inside it.
- Anything below the outer section moves down in turn.
- One `Ctrl+Z` restores collapse state, sibling position, and the outer
  frame's size together.

## Step: Collapsed frames nest by their visible bar

**Action:**
Collapse a section that is narrower than another expanded frame, and drag its
collapsed bar fully inside that frame. Then collapse the target frame.

**Expected:**
- The collapsed bar nests into the frame: collapsing the target hides it.
- Expanding the target shows the bar again, still collapsed, with its content
  intact.
- A collapsed bar *wider* than the target frame does not nest.

## Step: Deleting a collapsed section deletes its contents

**Action:**
Collapse a section that contains a few connected operators (ideally with a
connection passing through to an operator outside the frame). Select the
collapsed bar and press `Delete`. Then press `Ctrl+Z`. For comparison, also
delete an *expanded* section.

**Expected:**
- Deleting the collapsed bar removes the hidden operators (and any nested
  frames) with it — nothing pops back onto the canvas.
- A connection that ran through a deleted operator chain is bridged or
  removed, same as deleting those operators directly.
- One `Ctrl+Z` restores the section still collapsed, with all hidden
  operators and connections intact; expanding it shows the previous layout.
- Deleting an expanded section removes only the frame; its operators stay.

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
