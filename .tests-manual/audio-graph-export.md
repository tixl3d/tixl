---
id: audio-graph-export
title: Audio Graph — video render and executable export
scope: export
tags: [user, essential, hardware]
added: 2026-08-01
added-in-version: 4.3
prerequisites:
  - A project with graph audio that plays live — e.g. an [AudioToneGenerator]
    through an [AudioEcho] and an [AudioClip] with music, both into an
    [AudioBus] wired into the render chain.
---

Verifies that audio-graph sound survives both export paths: video render
(with effects, ducking and animated parameters baked into the file) and the
standalone executable (Player).

## Step: Graph audio in a rendered video

**Action:**
Confirm the graph sounds correctly live, then render a few seconds to a
video file via the export window.

**Expected:**
- The video's audio contains the graph sound: the tone with its effects and
  the music clip — not just a dry mix.
- Animated parameters (e.g. a keyframed volume or a duck) are audible in
  the file at the right times.
- Effects driven by [AudioReaction] respond in the rendered images.

## Step: Live audio intact after the render

**Action:**
After the render finishes, play the project again in the editor.

**Expected:**
- Everything sounds exactly as before the render — the music clip is still
  routed through the graph and the bus's `Level` output still moves.

## Step: Executable export finds the soundtrack

**Action:**
Set the music [AudioClip]'s `Display` to `BackgroundImage` (making it the
main soundtrack), then export the project as an executable.

**Expected:**
- No "No main soundtrack found" dialog appears.
- (Counter-check: with `Display = Clip` on all audio clips, the dialog
  *does* appear and explains how to mark a soundtrack.)

## Step: Executable plays graph audio and ends on time

**Action:**
Run the exported executable.

**Expected:**
- The music plays, including any graph effects, and audio-reactive visuals
  respond.
- The demo ends (or loops, when started with the loop option) when the
  soundtrack's content ends — it does not run on silently.
