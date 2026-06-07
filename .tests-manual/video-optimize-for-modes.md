---
id: video-optimize-for-modes
title: Video "Optimize For" Decode Modes
added: 2026-06-07
added-in-version: 4.2
scope: operators
tags: [essential, perf]
prerequisites:
  - A scratch project is open with an empty Graph Window.
  - A test video for the basic steps — the bundled `Operators/examples/Assets/videos/test-720p.mp4` works.
  - For the GPU and cadence steps, a heavier real-world clip helps make the difference visible — ideally a 1080p or 4K H.264/HEVC file, and a **23.976 fps** clip for the cadence check.
related-help:
  - ../.help/docs/operators/lib/io/video/PlayVideo.md
---

Covers the `Optimize For` parameter on `[PlayVideo]` and `[PlayVideoClip]`, which chooses how a
video stream is decoded. **Fast Seeking** (the default) decodes in software with a frame cache —
snappy scrub-back and smooth HD playback, with the GPU left free for the editor.
**Playback Performance** decodes zero-copy on the GPU — the smoothest playback of large/4K
footage, at the cost of slower random seeks. Both fall back to software when hardware decode is
unavailable. The editor must stay responsive in every mode (decode runs on a worker thread).

## Step: The Optimize For parameter is present with two modes

**Action:**
In the Graph Window press `Tab`, type `PlayVideo`, and place the operator. Set its `Path` to your
test video. Select the operator and look at the Parameter Window for the `Optimize For` input.

**Expected:**
- An `Optimize For` dropdown is shown, with **Fast Seeking** selected by default.
- Opening it offers exactly two choices: **Fast Seeking** and **Playback Performance**.

## Step: Fast Seeking plays smoothly and scrubs back instantly

**Action:**
With `Optimize For` on **Fast Seeking**, select the operator so the Output Window shows its
`Texture`, and play the timeline through a stretch of the clip. Then drag the playhead back and
forth across that same already-played stretch.

**Expected:**
- Playback is smooth and the editor's frame rate stays responsive.
- Scrubbing back over frames you already played is **instant** — no stutter and no progressive
  slow-down the further back you scrub (those frames come from the cache).

## Step: A 23.976 fps clip stays smooth (no cadence judder)

**Action:**
Set `Path` to a **23.976 / 24 fps** clip (most film-rate web rips). Keep `Optimize For` on
**Fast Seeking** and play for ~15 seconds, watching a moving part of the image.

**Expected:**
- Motion is steady — no periodic hitch that repeats every second or two.
- It looks as smooth as the same clip on **Playback Performance** (compare by switching the
  dropdown). A regression here would show as a recurring micro-stall on this frame rate only.

## Step: Playback Performance plays large footage smoothly

**Action:**
Set `Optimize For` to **Playback Performance** and play a large (1080p or 4K) clip.

**Expected:**
- Continuous playback is smooth, with low CPU use (decoding has moved to the GPU).
- The image is correct — colors and brightness match the same clip in Fast Seeking, not washed
  out or wrongly tinted (8-bit and 10-bit SDR footage both look right).

## Step: Switching modes during playback re-opens cleanly

**Action:**
While the timeline is playing, flip `Optimize For` between **Fast Seeking** and **Playback
Performance** a few times.

**Expected:**
- After a brief reload the video keeps playing in the newly selected mode.
- No crash, no red operator-error state, and no permanently black or frozen output.

## Step: Random seeking trades off by mode, editor never freezes

**Action:**
In each mode, jump the playhead to a far, not-yet-visited position, then back to a region you
already played.

**Expected:**
- **Fast Seeking:** the first jump to a fresh spot may take a moment to resolve, but returning to
  an already-visited region is instant.
- **Playback Performance:** jumps take longer to resolve (each re-decodes from a keyframe), but
  continuous playback stays the smoothest.
- In both modes the editor stays interactive during the seek — it shows the last valid frame and
  never freezes waiting for the decode.
