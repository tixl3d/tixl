---
id: timeline-editing
title: Timeline — playback follow, jumps and ripple editing
scope: timeline
tags: [user, essential]
added: 2026-08-02
added-in-version: 4.3
prerequisites:
  - A project with a few audio or video clips spread across the timeline.
---

Covers the timeline navigation and editing behaviors: follow-playback scrolling
with user-controlled view mode, stepping between cuts, ripple selection, and
splitting with automatic layout.

## Step: View follows playback

**Action:**
Zoom in so only part of the timeline is visible, then start playback and let the
time marker approach the right edge of the view.

**Expected:**
- Once the marker gets near the edge (roughly the outer 20%), the view scrolls
  along smoothly, keeping the marker visible.
- Zooming in or out during playback keeps the follow active at the new scale.

## Step: Panning takes over the view

**Action:**
While playback continues, drag-pan the timeline somewhere else. Then stop and
restart playback.

**Expected:**
- After panning, the view stays where you put it — no snapping back to the marker.
- Restarting playback re-engages the follow.
- The behavior can be disabled entirely via Settings → Timeline → "Follow playback
  time".

## Step: Step between cuts and keyframes

**Action:**
With the timeline paused, press `.` and `,` repeatedly.

**Expected:**
- The playhead steps to the next/previous clip start, clip end, or keyframe —
  whichever is nearest in that direction.
- When the target lies outside the visible range, the view scrolls to center it.

## Step: Ripple selection and gap editing

**Action:**
Cut a clip at the playhead (`Ctrl+X`), then press `Ctrl+Shift+A` (or right-click →
"Select following clips").

**Expected:**
- Every clip starting at or after the playhead is selected on all layers —
  including the right half of the cut you just made.
- Dragging the selection right opens a gap at the cut; dragging left closes one.

## Step: Split places the new op tidily

**Action:**
In the graph view, arrange two ops in a vertical column (e.g. two audio clips
feeding a bus). In the timeline, split the upper clip at the playhead.

**Expected:**
- The new op appears directly below the original, and any op that sat there moves
  down — nothing lands hidden underneath another op.
- The pushed ops stay aligned to the graph's grid, so snapped columns stay intact.
- The new clip keeps the original's connections into multi-inputs (e.g. it still
  feeds the same [AudioBus]).
- A single undo reverts the split including all pushed positions.

## Step: Renamed clip labels

**Action:**
Rename an audio clip op (e.g. to "Voice Over"), then hover its clip in the
timeline.

**Expected:**
- The clip label shows the custom name in quotes; the referenced audio file's name
  appears in the hover tooltip instead.
- Labels are readable at rest, brighten slightly on hover, and use the selection
  color when selected.
