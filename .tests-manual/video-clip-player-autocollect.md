---
id: video-clip-player-autocollect
title: Video Clip Player — Auto-Collect
added: 2026-06-07
added-in-version: 4.2
scope: operators
tags: [essential]
prerequisites:
  - A scratch project is open with an empty Graph Window.
  - The "Video Clip Player — Wired Clips" set has been run, or you are otherwise familiar with placing `[VideoClip]`s on the timeline.
  - Two or three short, visually distinct test videos. Frame-numbered clips help.
related-help:
  - ../.help/docs/operators/lib/io/video/VideoClipPlayer.md
---

Verifies `[VideoClipPlayer]`'s `AutoCollect` mode: with it on, the player composites sibling `[VideoClip]`s in
the same composition **without any wiring**, by scanning and driving them. This is the timeline-NLE path —
drop clips on the timeline and they play through one player. Covers the on/off toggle, continuous cuts,
wired-plus-scanned de-duplication, and layer order.

## Step: Unwired clips compose only when AutoCollect is on

**Action:**
In a composition, place a `[VideoClipPlayer]` and two `[VideoClip]`s with different `Path`s. Do **not** wire
the clips into the player. Position them as adjacent clips on the timeline. Select the player to view its
`ColorBuffer`. First, with the player's `AutoCollect` input **off**, scrub across the clips; then turn
`AutoCollect` **on** and scrub again.

**Expected:**
- With `AutoCollect` **off**, the player's output is empty across the clips — nothing is wired in.
- With `AutoCollect` **on**, the clip under the playhead is composited, exactly as if it had been wired.

## Step: Continuous playback across unwired cuts

**Action:**
With `AutoCollect` on, play the timeline forward across the sequence of clips at normal speed.

**Expected:**
- The output cuts from one clip to the next at each boundary, producing one continuous program.
- No transparent gap appears at the cuts — preroll warms the upcoming clip for scanned clips too.

## Step: A clip both wired and auto-collected draws once

**Action:**
Keep `AutoCollect` on. Now also drag one of the two clips' `Texture` into the player's `VideoClips` input, so
that clip is both wired and a scannable sibling. Scrub into that clip's range (and, if you can, overlap it
with the other clip so both are active at once).

**Expected:**
- The wired-and-scanned clip appears exactly once — it is not doubled, brightened, or blended with itself.
- The other, scan-only clip still appears normally.

## Step: Layer order holds for auto-collected clips

**Action:**
Overlap two unwired clips on different timeline layers, with `AutoCollect` on. Scrub into the overlap.

**Expected:**
- Both clips contribute, and the **lower** `LayerIndex` is on top — the same stacking rule as wired clips.

## Step: Toggling AutoCollect off falls back to wired-only

**Action:**
With one clip wired and one scan-only, and both currently visible, turn `AutoCollect` off.

**Expected:**
- Only the wired clip remains in the output; the scan-only clip drops out immediately.
- Turning `AutoCollect` back on restores it.
