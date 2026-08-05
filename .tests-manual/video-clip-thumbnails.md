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
- After a moment, a small video frame appears at the left edge of the clip (the clip's in-point frame)
  and another at the right edge (the out-point frame).
- The editor does not stutter while the thumbnails are generated.
- Very narrow clips or very flat layers show no thumbnails.

## Step: Previewing the frame under the mouse

**Action:**
Hover the body of a video clip and slowly move the mouse along it.

**Expected:**
- The tooltip shows a thumbnail of the video frame near the hovered position, above the usual text info.
- Moving along the clip updates the thumbnail (quantized to roughly quarter-second steps; it may lag
  slightly behind the mouse on long-GOP footage).
- Scrubbing quickly back and forth never freezes or stutters the timeline.

## Step: Thumbnails follow a trim

**Action:**
Trim the clip's start handle to a different position and release the mouse.

**Expected:**
- While dragging, the UI stays smooth.
- After releasing, the left thumbnail updates to the new in-point frame after a moment.

## Step: Start/end thumbnails persist across restarts

**Action:**
Close and restart the editor, then open the same project and look at the video clip.

**Expected:**
- The start/end thumbnails reappear quickly (loaded from the disk cache) without noticeable decoding delay.
