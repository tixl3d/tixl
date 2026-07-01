---
id: video-playback-determinism
title: Video Playback Determinism (FFmpeg)
added: 2026-06-03
added-in-version: 4.2
scope: operators
tags: [user, essential, perf]
prerequisites:
  - A scratch project is open with an empty Graph Window.
  - A test video is available. The bundled `Operators/examples/Assets/videos/test-720p.mp4` works for every step; for the frame-accuracy and export steps, a clip with **burned-in frame numbers** (a frame counter or a clock with a visible frame digit) makes the checks unambiguous.
---

This checks that video playback in `[PlayVideo]` and `[PlayVideoClip]` is **repeatable**: the same point
in time always shows the same frame, pausing and playing again doesn't shift the frame, and rendering to
a file commits the exact frame for each output frame. It also covers looping vs. holding on the last
frame at the clip's end, and that a frame that isn't ready yet shows the last good frame instead of
freezing the editor. Playback should never stall the editor's frame rate.

## Step: First frame appears and plays back

**Action:**
In the Graph Window press `Tab`, type `PlayVideo`, and place the operator. In the Parameter Window set
its `Path` input to your test video (use the file picker on the `Path` field). Select the operator so the
[ui:OutputWindow|Output Window] shows its image, then start the timeline playing.

**Expected:**
- A frame of the video is visible in the Output Window within a moment of setting the path — not a black
  or empty image.
- With the timeline playing, the image advances smoothly.
- The editor's own frame rate (status bar / FPS) stays responsive while the video plays — playback does
  not stall the editor, even on a large clip.

## Step: About dialog reports the bundled video engine

**Context:** Run this after at least one video has played this session — the video engine starts up on
first use, so the line is absent until a video operator has loaded.

**Action:**
Open the main menu and choose **About TiXL** (or click the TiXL logo, then About). Read the System
Information block. Then click **Copy System Information** — it copies the same text to your clipboard.

**Expected:**
- A line reads `FFmpeg: <version> (LGPL)` — for example `FFmpeg: 7.0 (LGPL)`.
- The version line shown in the About dialog is the same line that gets copied (paste it anywhere to
  confirm, or just read it from the dialog).

## Step: Scrubbing is deterministic — same time, same frame

**Action:**
Stop playback. On the `[PlayVideo]` operator enable the `OverrideTimeInSecs` input (right edge of the
field, or animate/type a value) and set it to a fixed value such as `2.0`. Note the exact frame shown.
Change `OverrideTimeInSecs` to `5.0`, then back to `2.0`.

**Expected:**
- At `2.0` the output is a specific frame; returning to `2.0` from elsewhere shows the **identical** frame
  (with a frame-numbered clip, the same number both times).
- Sweeping `OverrideTimeInSecs` up and back down lands on the same frames going down as it did going up —
  no drift, no off-by-one between directions.
- Setting the same value twice never produces two different frames.

## Step: Paused-then-play has no frame offset

**Action:**
With the timeline stopped, position it on a clearly identifiable frame (use a frame-numbered clip, or note
a distinctive moment). Read the frame in the Output Window. Now press play and immediately pause again on
the same timeline position.

**Expected:**
- The frame shown while paused matches the frame shown the instant playback starts — there is no jump
  forward or backward at the play/pause transition.
- There should be no momentary "wrong frame" flash on the first played frame.

## Step: Loop on wraps; loop off clamps to the last frame

**Action:**
Make the timeline (or `OverrideTimeInSecs`) run past the clip's `Duration` output value. First with the
`Loop` input **off**, then with `Loop` **on**.

**Expected:**
- With `Loop` **off**, time past the end holds on the **last** frame of the clip (and `HasCompleted` reads
  true); time before the start holds on the first frame.
- With `Loop` **on**, time past the end wraps back into the clip (the first frame follows the last) and
  keeps cycling; `HasCompleted` does not latch.
- The wrap is seamless — no black frame or stall at the loop point.

## Step: Fast scrubbing returns the last valid frame, never freezes

**Action:**
Drag the timeline playhead back and forth quickly across the whole clip (or sweep `OverrideTimeInSecs`
rapidly), faster than the video can keep up.

**Expected:**
- The Output Window keeps showing a valid (slightly behind) frame and the editor stays interactive — it
  never freezes waiting.
- When you stop moving, the image settles onto the exact frame for the final position within a fraction
  of a second.
- No error dialog or red error state on the operator appears from the rapid scrubbing.

## Step: PlayVideoClip honours the clip's source range on the timeline

**Action:**
Add a `[PlayVideoClip]` operator and drop it as a clip on the timeline. Set its `Path`. Trim and/or scale
the clip on the timeline, then scrub across it.

**Expected:**
- The frame shown follows the clip's trimmed range — the start of the trimmed clip shows the frame at the
  trim-in point, not the file's first frame.
- Scrubbing within the clip stays frame-accurate and repeatable, the same as `[PlayVideo]`.
- Scaling the clip stretches the timing (slower/faster source advance) without breaking repeatability —
  returning to a timeline position shows the same source frame.

## Step: Render-to-file is frame-aligned (no duplicated first frame)

**Action:**
Connect a `[PlayVideo]` (or a `[PlayVideoClip]` on the timeline) to an output and render a short range to
a file via the Render/Export window. Use a frame-numbered clip if available. Open the rendered file and
step through the first several frames.

**Expected:**
- The first rendered frame is the **correct** source frame for the render's start time — not a stale or
  repeated frame left over from the editor's last position.
- Each successive rendered frame maps 1:1 to the expected source frame — no duplicated, skipped, or
  out-of-order frames through the range.
- The export waits for each exact frame; the result is identical across two renders of the same range.
