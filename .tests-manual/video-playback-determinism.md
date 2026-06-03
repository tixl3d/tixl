---
id: video-playback-determinism
title: Video Playback Determinism (FFmpeg)
added: 2026-06-03
added-in-version: 4.2
scope: operators
tags: [essential, perf]
prerequisites:
  - A scratch project is open with an empty Graph Window.
  - A test video is available. The bundled `Operators/examples/Assets/videos/test-720p.mp4` works for every step; for the frame-accuracy and export steps, a clip with **burned-in frame numbers** (a frame counter or a clock with a visible frame digit) makes the checks unambiguous.
---

Verifies the FFmpeg decode path that replaced Media Foundation in `[PlayVideo]` and
`[PlayVideoClip]`. The core guarantee is **determinism**: the same playback time always
resolves to the same source frame, pausing then playing introduces no frame offset, and
render-to-file commits the exact frame for each output frame. The set also covers the
loop-vs-clamp boundary and that a not-yet-decoded frame returns the last valid texture
instead of freezing the UI. Decode runs on a worker thread, so playback should never block
the editor's frame rate.

## Step: First frame appears and plays back

**Action:**
In the Graph Window press `Tab`, type `PlayVideo`, and place the operator. In the
Parameter Window set its `Path` input to your test video (use the file picker on the
`Path` field). Select the operator so the Output Window shows its `Texture` output, then
start the timeline playing.

**Expected:**
- A frame of the video is visible in the Output Window within a moment of setting the path
  — not a black/empty texture.
- With the timeline playing, the image advances smoothly.
- The editor's own frame rate (status bar / FPS) stays responsive while the video plays —
  decoding does not stall the UI even on a large clip.

## Step: About dialog reports the bundled FFmpeg build

**Context:** Run this after at least one video has played this session — FFmpeg initialises
lazily on first use, so the line is absent until a video op has loaded.

**Action:**
Open the main menu and choose **About TiXL** (or click the TiXL logo, then About). Read the
System Information block. Then click **Copy System Information** and paste into a text editor.

**Expected:**
- A line reads `FFmpeg: <version> (LGPL)` — for example `FFmpeg: 7.0 (LGPL)`. On a developer
  machine running a GPL/non-free build it instead reads `(GPL/non-free — development build)`.
- The same `FFmpeg:` line is present in the copied System Information text.

## Step: Scrubbing is deterministic — same time, same frame

**Action:**
Stop playback. On the `[PlayVideo]` operator enable the `OverrideTimeInSecs` input (right
edge of the field, or animate/type a value) and set it to a fixed value such as `2.0`. Note
the exact frame shown. Change `OverrideTimeInSecs` to `5.0`, then back to `2.0`.

**Expected:**
- At `2.0` the output is a specific frame; returning to `2.0` from elsewhere shows the
  **identical** frame (with a frame-numbered clip, the same number both times).
- Sweeping `OverrideTimeInSecs` up and back down lands on the same frames going down as it
  did going up — no drift, no off-by-one between directions.
- Setting the same value twice never produces two different frames.

## Step: Paused-then-play has no frame offset

**Action:**
With the timeline stopped, position it on a clearly identifiable frame (use a frame-numbered
clip, or note a distinctive moment). Read the frame in the Output Window. Now press play and
immediately pause again on the same timeline position.

**Expected:**
- The frame shown while paused matches the frame shown the instant playback starts — there
  is no jump forward or backward at the play/pause transition.
- This is the regression the FFmpeg path fixes versus the old Media Foundation start-offset
  priming; there should be no momentary "wrong frame" flash on the first played frame.

## Step: Loop on wraps; loop off clamps to the last frame

**Action:**
Make the timeline (or `OverrideTimeInSecs`) run past the clip's `Duration` output value.
First with the `Loop` input **off**, then with `Loop` **on**.

**Expected:**
- With `Loop` **off**, time past the end holds on the **last** frame of the clip (and
  `HasCompleted` reads true); time before the start holds on the first frame.
- With `Loop` **on**, time past the end wraps back into the clip (the first frame follows
  the last) and keeps cycling; `HasCompleted` does not latch.
- The wrap is seamless — no black frame or stall at the loop point.

## Step: Fast scrubbing returns the last valid frame, never freezes

**Action:**
Drag the timeline playhead back and forth quickly across the whole clip (or sweep
`OverrideTimeInSecs` rapidly), faster than the video can decode.

**Expected:**
- The Output Window keeps showing a valid (slightly stale) frame and the editor stays
  interactive — it never freezes waiting for a decode.
- When you stop moving, the image settles onto the exact frame for the final position
  within a fraction of a second.
- No exception dialog or red operator-error state appears from the rapid seeking.

## Step: PlayVideoClip honours the clip's source range on the timeline

**Action:**
Add a `[PlayVideoClip]` operator and drop it as a clip on the timeline (it appears as a
TimeClip). Set its `Path`. Trim and/or scale the clip on the timeline, then scrub across it.

**Expected:**
- The frame shown maps through the clip's `TimeRange`/`SourceRange` — the start of the
  trimmed clip shows the source frame at the source-range start, not the file's first frame.
- Scrubbing within the clip stays frame-accurate and deterministic, the same as `[PlayVideo]`.
- Scaling the clip stretches the mapping (slower/faster source advance) without breaking
  determinism — returning to a timeline position shows the same source frame.

## Step: Render-to-file is frame-aligned (no duplicated first frame)

**Action:**
Wire a `[PlayVideo]` (or a `[PlayVideoClip]` on the timeline) into an output and render a
short range to a file via the Render/Export window. Use a frame-numbered clip if available.
Open the rendered file and step through the first several frames.

**Expected:**
- Output frame 0 is the **correct** source frame for the render's start time — not a stale
  or repeated frame left over from the editor's last position (the earlier
  `0960 0000 0001…` first-frame artefact must not reappear).
- Each successive output frame maps 1:1 to the expected source frame — no duplicated,
  skipped, or out-of-order frames through the range.
- The export waits for each exact frame (via `Playback.OpNotReady`); the result is identical
  across two renders of the same range.
