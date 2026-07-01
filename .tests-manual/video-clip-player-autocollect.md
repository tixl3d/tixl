---
id: video-clip-player-autocollect
title: Video Clip Player — Auto-Collect
added: 2026-06-07
added-in-version: 4.2
scope: operators
tags: [user, essential]
prerequisites:
  - A scratch project is open with an empty Graph Window.
  - The "Video Clip Player — Wired Clips" set has been run, or you are otherwise familiar with placing `[VideoClip]`s on the timeline.
  - Two or three short, visually distinct test videos. Frame-numbered clips help.
related-help:
  - ../.help/docs/operators/lib/io/video/VideoClipPlayer.md
---

This checks the `AutoCollect` option on `[VideoClipPlayer]`. With it on, the player automatically
shows the `[VideoClip]`s that sit in the same composition — you don't have to connect them to the
player one by one. It's the timeline editing workflow: drop clips on the timeline and they play
through a single player, cutting from one to the next. This set covers the on/off switch, clean cuts,
not double-drawing a clip that's both connected and auto-collected, and the stacking order.

## Step: Unwired clips compose only when AutoCollect is on

**Action:**
In a composition, place a `[VideoClipPlayer]` and two `[VideoClip]`s with different `Path`s. Do **not**
connect the clips to the player. Position them as adjacent clips on the [ui:Timeline|timeline]. Select the player so
the [ui:OutputWindow|Output Window] shows its image. First, with the player's `AutoCollect` input **off**, scrub across
the clips; then turn `AutoCollect` **on** and scrub again.

**Expected:**
- With `AutoCollect` **off**, the player shows nothing across the clips — nothing is connected to it.
- With `AutoCollect` **on**, the clip under the playhead appears, exactly as if you had connected it.

## Step: Continuous playback across unwired cuts

**Action:**
With `AutoCollect` on, play the timeline forward across the sequence of clips at normal speed.

**Expected:**
- The image cuts from one clip to the next at each boundary, playing as one continuous program.
- No empty gap appears at the cuts — the next clip is ready in time, even for auto-collected clips.

## Step: A clip both wired and auto-collected draws once

**Action:**
Keep `AutoCollect` on. Now also connect one of the two clips to the player's `VideoClips` input, so
that clip is both connected **and** auto-collected. Scrub into that clip's range (and, if you can,
overlap it with the other clip so both are active at once).

**Expected:**
- The clip appears exactly once — it is not doubled, brightened, or blended with itself.
- The other, auto-collected-only clip still appears normally.

## Step: Layer order holds for auto-collected clips

**Action:**
Overlap two unconnected clips on different timeline layers, with `AutoCollect` on. Scrub into the overlap.

**Expected:**
- Both clips contribute, and the clip on the **lower** `LayerIndex` is on top — the same stacking rule
  as connected clips.

## Step: Toggling AutoCollect off falls back to wired-only

**Action:**
With one clip connected and one auto-collected only, and both currently visible, turn `AutoCollect` off.

**Expected:**
- Only the connected clip stays in the image; the auto-collected-only clip drops out immediately.
- Turning `AutoCollect` back on brings it back.
