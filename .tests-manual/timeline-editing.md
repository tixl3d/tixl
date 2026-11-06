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

## Step: Curve Mode indicator reflects keyframes on either side

**Action:**
In the timeline toolbar, watch the text color of the mode button.
Move the playhead so selected parameters have keyframes only before it, then only after it.
Finally, remove their keyframes.

**Expected:**
- The mode button text uses the active text color when keyframes exist before or after the playhead.
- The mode button text is muted only when no keyframes exist on either side.

## Step: Ripple selection and gap editing

**Action:**
Cut a clip at the playhead (`Ctrl+X`), then press `Ctrl+Shift+A` (or right-click →
"Select Following Clips").

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

## Step: Split only renames renamed clips

**Action:**
Split an op that still carries its symbol name (e.g. an [AudioClip] labelled
`AudioClip`). Then rename another op to `Voice Over` and split that one too.

**Expected:**
- The first split's new op has **no** custom name — both halves still show
  `AudioClip`, and its title is not shown in quotes in the graph.
- The second split's new op is named `Voice Over2`.

## Step: Duplicate clips from the timeline

**Action:**
Click the timeline window so it has focus, select one clip (or two on different
layers), and press `Ctrl+D`. Repeat via right-click → **Duplicate**.

**Expected:**
- One copy per selected clip appears at exactly the same start and end time, on
  the next free layer — never stacked on top of the original.
- The selection moves to the copies; the originals are deselected. With two clips
  selected, **both** copies end up selected.
- In the graph, each new op sits directly below its original, and any op that sat
  there is pushed down by whole grid rows.
- The copies keep the originals' connections into multi-inputs.
- A single undo removes all copies and restores the pushed positions.

## Step: Ctrl+D does not collide with keyframe duplication

**Action:**
In the dope sheet, select a few keyframes with no clip selected and press
`Ctrl+D`. Then select a clip (which clears the keyframe selection) and press
`Ctrl+D` again.

**Expected:**
- The first press duplicates the keyframes only — no new clip op is created.
- The second press duplicates the clip only.

## Step: Renamed clip labels

**Action:**
Rename an audio clip op (e.g. to "Voice Over"), then hover its clip in the
timeline.

**Expected:**
- The clip label shows the custom name in quotes; the referenced audio file's name
  appears in the hover tooltip instead.
- Labels are readable at rest, brighten slightly on hover, and use the selection
  color when selected.

## Step: Clip context menu order and styling

**Action:**
Select exactly one non-audio clip (e.g. a [Layer] op) in the timeline, park the
playhead inside it, and right-click the clip.

**Expected:**
- The rows appear in this order, top to bottom: **Select Following Clips**,
  **Cut at Time**, **Duplicate**, **Edit Clip Times**, **Clear Time Stretch**,
  **Delete**.
- Every label is Title Case, and the labels all start on the same x-position —
  including the keyframe rows further down (**Paste Keyframes**, **View All**).
- Shortcuts are right-aligned and dimmed: `Ctrl+Shift+A` on Select Following
  Clips, `Ctrl+X` on Cut at Time, `Ctrl+D` on Duplicate, `Delete` on Delete.
- Rows highlight with a rounded background on hover; disabled rows (e.g. **Paste
  Keyframes** with an empty clipboard) stay dim and show no hover highlight.
- Because this clip is neither an audio clip nor a data clip, there is exactly
  one separator between **Delete** and the keyframe rows — no empty gap with two
  separator lines stacked.

## Step: Contextual clip rows appear only when they apply

**Action:**
Right-click a single selected [AudioClip] that is *not* the main soundtrack.
Then right-click a clip of any other type.

**Expected:**
- For the audio clip, a **Set as Main Soundtrack** row appears below a separator,
  under **Delete**.
- For the other clip, that row is absent and no leftover separator remains.

## Step: Trimming previews the cut frame

**Action:**
With playback stopped, place the playhead somewhere outside a video clip. Drag the clip's start
handle left and right, then its end handle. Release, and repeat once while holding `Shift`
(snapping off) and once with playback running.

**Expected:**
- While a handle is dragged, the output shows the frame at that edge: the first frame for the start
  handle, the last frame for the end handle.
- The playhead stays where it was and is drawn faded during the drag; the trim edge still snaps to it.
- On release the playhead is back at full opacity and the output shows its frame again.
- A plain click on a handle (no drag) does not change the output.
- Dragging the end handle past the end of the footage holds the last frame; dragging the start handle
  before the footage holds the first frame. The console shows no FFmpeg decode errors.
- During playback, trimming does not jump the output; playback continues from the playhead.

## Step: Dragging toward the edge scrolls the timeline

**Action:**
Zoom in so only a few bars are visible. Drag a clip body slowly toward the right edge of the timeline
and past it, hold it there for a moment, then drag it back and release. Repeat with a clip's trim
handle, a keyframe in the dope sheet, a keyframe in Curve mode, a selection range handle, a loop range
handle and the playhead in the ruler, toward both the left and the right edge.

**Expected:**
- About 30 px before the edge the view starts scrolling; the further the pointer goes, the faster it scrolls.
- The dragged element stays under the pointer while the view scrolls, also when the pointer is outside
  the window.
- A dragged clip stays selected and keeps following the pointer; on release it lands where it was
  dropped and the move can be undone in one step.
- A drag that starts near an edge does not scroll until the pointer moves toward that edge.
- A clip drag that has locked to moving between layers does not scroll the timeline.
