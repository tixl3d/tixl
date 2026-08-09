---
id: video-clip-thumbnails
title: Video Clip Thumbnails
scope: timeline
tags: [video, timeline]
added: 2026-08-05
added-in-version: 4.2
prerequisites:
  - A project with at least one [VideoClip] on the timeline, referencing a video file of ~30s or longer.
---

Verifies the thumbnails shown on and around video clips in the timeline: the persistent
first/last-frame thumbnails inside the clip body, and the session-only frame preview in
the hover tooltip. All decoding runs on a background worker, so the UI must stay responsive
throughout.

## Step: Seeing start and end thumbnails on the clip body

**Action:**
Make the timeline layers reasonably tall (drag the layer-height handle up if needed) and look
at a video clip that is at least a few thumbnail-widths wide.

**Expected:**
- After a moment, a video frame appears at the left edge of the clip (the clip's in-point frame)
  and another at the right edge (the out-point frame), with subtly rounded corners.
- On flat layers the title sits vertically centered between the two thumbnails; on layers taller than
  about two small-text rows the title moves into its own row above them.
- The editor does not stutter while the thumbnails are generated.

## Step: Behavior on narrow clips

**Action:**
Zoom the timeline out (or trim a clip short) so a video clip becomes narrower than two thumbnails,
then narrower than the title.

**Expected:**
- The end thumbnail slides behind the start thumbnail and fades out as the overlap grows.
- Long titles are shortened with ".." instead of being cut off hard; as the clip gets very narrow the
  title fades out entirely.

## Step: No dimming of unconnected media clips

**Action:**
Disconnect a video or audio clip from its consumer and move the mouse off it, then hover it.

**Expected:**
- The clip does not dim when unconnected — thumbnails and waveforms stay readable.
- The clip renders slightly translucent when not hovered and fully solid on hover or selection.
- The tooltip still shows the "(Not connected?)" hint.

## Step: Thumbnails stay bounded while working with clips

**Action:**
Open Windows > Utilities > Thumbnails and note the "Atlas cells used" counter. Then hover, drag and
scrub along several video clips for a while and check the counter again.

**Expected:**
- The counter grows by roughly two cells per distinct clip in/out point and then stops — hovering and
  scrubbing add nothing, and dragging a clip along the timeline adds nothing.
- It never approaches its cap during normal editing.

## Step: Thumbnails follow a trim

**Action:**
Trim the clip's start handle to a different position and release the mouse.

**Expected:**
- While dragging, the UI stays smooth.
- After releasing, the left thumbnail updates to the new in-point frame after a moment.

## Step: Source region appears in the ruler

**Action:**
Hover a video or audio clip, then select it (single selection) and move the mouse away.

**Expected:**
- While hovered — and while it is the only selected clip — a translucent region appears in the ruler
  behind the selection-range line, spanning the clip's full source footage.
- No footage outline is drawn around the clip body anymore.
- Media clips show no remap indicator line at their bottom edge and no remap source curves into the ruler.

## Step: Slipping footage via the source region

**Action:**
With a trimmed video clip selected (so the region extends past the white selection-range line), drag the
part of the region left or right of the white line horizontally. Then press `Ctrl + Z`.

**Expected:**
- The cursor becomes a pointer over the draggable region parts.
- Dragging slides the footage under the clip (thumbnails update after release); the clip itself does not
  move and its speed is unchanged.
- The region's edges snap to other clips and the playhead while dragging; `Shift` bypasses snapping.
- Undo restores the previous slip position.

## Step: Selection-range handles trim clips instead of stretching

**Action:**
Select a video clip together with some keyframes, then drag the selection-range start handle to the right
past the clip's start, and back again.

**Expected:**
- The clip's start edge is trimmed at the handle position — its content does not change speed.
- Dragging the handle back restores the clip's original extent during the same drag.
- Keyframes in the selection still stretch proportionally as before.
- Undo reverts both the trim and the keyframe stretch in one step.

## Step: Source region responds to hover

**Action:**
Move the mouse over the draggable part of the ruler source region, then away from it.

**Expected:**
- The region's outline brightens while a draggable part is hovered (or while dragging) and dims otherwise.

## Step: Auto-collect indicator lines in the graph

**Action:**
In the graph, place a [VideoClipPlayer] with AutoCollect enabled next to unwired [VideoClip] ops.
Hover and select the clips and the player. Then wire one clip into the player's input.

**Expected:**
- A faint texture-colored curve connects each unwired (auto-collected) [VideoClip] to the player.
- The curve brightens while either the clip or the player is hovered or selected.
- A clip that is wired into the player (directly or through an inserted effect) shows no curve.
- Disabling AutoCollect removes all curves.

## Step: Playhead past the end of the footage

**Action:**
Take a clip of long, high-resolution footage (4K, long-GOP), drag its end handle well past the end of the
available footage, and park the playhead in that extended part. Watch the editor's CPU usage for ~10 s,
then scrub around inside the extended part.

**Expected:**
- The clip shows the footage's last frame, held.
- CPU usage stays at idle levels — the frame is decoded once, not re-decoded every frame.
- Scrubbing back into the real footage picks up normally.

## Step: Start/end thumbnails persist across restarts

**Action:**
Close and restart the editor, then open the same project and look at the video clip.

**Expected:**
- The start/end thumbnails reappear quickly (loaded from the disk cache) without noticeable decoding delay.
