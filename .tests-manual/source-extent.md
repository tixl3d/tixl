---
id: source-extent
title: Time clips — authored source extent
added: 2026-08-09
added-in-version: 4.3
scope: timeline
prerequisites:
  - A project with a composition containing at least two animated ops (a few keyframes each).
---

Combined time-clip operators can now carry an authored **source extent** — the span of their
meaningful content in bars. It is edited inside the symbol via handles in the timeline ruler,
initialized automatically by **Combine into New Type...** (as time clip), and shown as the footage
region when the clip is used in a parent timeline. The old Alt-drag range manipulation inside a
clip is removed (viewing the shaded range remains).

## Step: Combine initializes the extent

**Action:**
In a composition, animate two ops with keyframes between bar 2 and bar 6. Select both, right-click →
**Combine into New Type...**, check **Combine as time clip**, and combine as `MyTransition`.
Double-click the new clip on the timeline to enter it.

**Expected:**
- The ruler shows a dark band with a thin vertical handle at bar 2 and one at bar 6 (the union of
  the combined keyframes).
- The clip instance in the parent timeline was created with exactly this range as its source range.

## Step: Drag the extent handles with undo

**Action:**
Inside `MyTransition`, hover the right extent handle at bar 6 in the ruler.

**Expected:**
- The mouse cursor changes to a horizontal-resize cursor; the handle brightens to full white.

**Action:**
Drag the handle right to bar 8, release. Then press **Ctrl+Z**.

**Expected:**
- While dragging, the band follows the mouse and snaps to raster values (hold **Shift** to bypass).
- After release the band ends at bar 8; after **Ctrl+Z** it is back at bar 6. **Ctrl+Shift+Z**
  restores bar 8.
- Existing clip instances in the parent timeline keep their current in/out — changing the extent
  never retimes placed clips.

## Step: Footage region in the parent timeline

**Action:**
Leave `MyTransition` and hover its clip in the parent timeline.

**Expected:**
- The tooltip shows a `Footage: 2.00 ... 8.00` line (matching the authored extent).
- The ruler shows the footage region behind the selection-range bar, sized to the extent — the same
  display video clips get.

**Action:**
Drag the clip's end handle to make it longer than the extent maps to.

**Expected:**
- The tooltip's footage line appends `(reads past end — loops/freezes)`.

**Action:**
Drag the footage region left/right in the ruler (slip edit) while the clip is selected.

**Expected:**
- The clip's source range shifts (content slides under the fixed clip window); undo restores it.

## Step: Reset Source to Extent

**Action:**
In the parent timeline, slip-drag `MyTransition`'s footage region so its source range no longer
matches the extent (tooltip `Source` differs from `Footage`). Right-click the clip.

**Expected:**
- The context menu shows **Reset Source to Extent** below **Clear Time Stretch** (the item is
  absent for clips whose symbol has no authored extent, e.g. a plain `[TimeClip]`).

**Action:**
Click it, then hover the clip; press **Ctrl+Z**.

**Expected:**
- After the click the tooltip's `Source` line equals the `Footage` line exactly.
- **Ctrl+Z** restores the slipped source range.

## Step: [TimeClipPlayer] auto-collects unwired command clips

**Action:**
In a composition rendered through a `[Group]`, add a `[TimeClipPlayer]` and wire its output into the
Group. Add a `[TimeClip]` op (or place `MyTransition`) on the timeline **without** connecting its
output to anything. Move the playhead inside the clip's range.

**Expected:**
- The clip's content renders even though the clip is unwired.
- In the graph, a faint magenta (command-colored) curve runs from the clip to the `[TimeClipPlayer]`;
  it brightens when either op is hovered or selected.

**Action:**
Wire the clip's output directly into the Group as a second input. Then set the player's
**AutoCollect** to off and remove the wire again.

**Expected:**
- Wired: the indicator line disappears and the clip renders exactly once (via the wire — no
  double-draw).
- AutoCollect off and unwired: the clip stops rendering and no indicator lines are drawn.

**Action:**
Place two unwired clips on different timeline rows, overlapping in time, with different content.

**Expected:**
- The clip on the upper row draws on top of the one on the lower row.

## Step: Alt-drag inside a clip no longer edits

**Action:**
Enter `MyTransition` again, hold **Alt**, and try dragging near the shaded range boundary at the
bottom of the timeline (the pre-4.3 range-edit interaction).

**Expected:**
- The orange shading outside the source range still draws, but no drag handles appear and holding
  Alt changes nothing — the range is edited via the extent handles or the parent-timeline slip drag.
