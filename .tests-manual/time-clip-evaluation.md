---
id: time-clip-evaluation
title: Time Clip Evaluation — consistent remap on every output
added: 2026-08-08
added-in-version: 4.3
scope: timeline
prerequisites:
  - A project is open with its own Composition Settings enabled.
  - A short video file (5–30s, .mp4 or similar) is available on disk.
  - Optionally a short .mid file for the MIDI steps.
related-help:
  - ../.help/docs/using/Timeline.md
---

A time clip now remaps time for the **whole operator**, not just for the consumer that pulls its
time-clip output. Animated parameters on a `[VideoClip]` follow the clip when it's moved or slipped,
`[MidiClip]` no longer double-remaps through its `Values` output, and stretching a clip stretches the
keyframes inside it. This set verifies the remap is applied consistently and that everyday clip
workflows are unchanged.

## Step: Video clip plays normally (regression)

**Action:**
Drop a video file onto the timeline's clip area so a `[VideoClip]` is created, and wire it into a
`[VideoClipPlayer]` (or enable the player's AutoCollect). Play across the clip; scrub back and forth.

**Expected:**
- The video plays exactly along the clip's extent, starting at its first frame.
- Scrubbing shows the matching frames; no offset from before.

## Step: Slipped clip shows slipped footage through both outputs

**Action:**
Drag inside the clip body with `Ctrl + Alt` (slip) to shift the source region by a second or two.
Scrub across the clip while it's composited by the player. Then also wire the clip's **time-clip
output** into a `[Group]` and pin that group's output — compare a few frames at the same playhead
positions.

**Expected:**
- Both consumers show the *same* frame at the same playhead position.
- The footage is shifted by the slipped amount in both cases.

## Step: Animated parameter travels with the clip

**Action:**
On the `[VideoClip]`, animate `Color`'s alpha to fade in over the first bar of the clip
(`Alt + click` the parameter to add keyframes at the clip start and one bar in). Verify the fade
plays. Now drag the whole clip two bars later on the timeline and play again.

**Expected:**
- The fade-in happens at the *new* clip start — the keyframes travelled with the clip.
- The dope sheet shows the keys at their position inside the clip.

## Step: Stretching a clip stretches its animation

**Action:**
With the same faded clip, drag the clip's end handle while holding `Alt` (stretch — content scales
to fit). Double the clip's length. Play across it.

**Expected:**
- The fade-in now takes two bars instead of one — the animation stretched with the content.
- Without `Alt`, trimming keeps the playback rate and only reveals/hides footage; the fade keeps
  its original one-bar duration.

## Step: Preroll still warms the decoder (regression)

**Action:**
Place two `[VideoClip]`s back-to-back (a hard cut), both drawn by one `[VideoClipPlayer]`. Play
across the cut a few times.

**Expected:**
- The incoming clip's first frame appears exactly at the cut — no transparent/black blink while the
  decoder starts up.

## Step: Export is frame-exact (regression)

**Action:**
Render a few seconds around the cut (and the faded clip) to a video file via Render-to-file. Step
through the result around the cut.

**Expected:**
- The exported frames match the editor preview; the cut lands on the same frame; the fade matches.

## Step: MidiClip timing is identical through all outputs

**Action:**
(Skip if no .mid file at hand.) Create a `[MidiClip]` with a MIDI file, note when its first events
fire during playback. Slip/stretch the clip so `SourceRange != TimeRange`, and compare the event
timing consumed through `Values` against the clip's position on the timeline.

**Expected:**
- Events fire when the playhead crosses their position inside the clip — the clip's stretch is
  applied exactly once (previously the `Values` output applied it twice).

## Step: Disable / re-enable a clip (regression)

**Action:**
Select the `[VideoClip]` and disable it (shortcut or context menu), then re-enable it. Play.

**Expected:**
- Disabled: the clip doesn't render.
- Re-enabled: plays exactly as before; no console warnings about an "invalid time clip update
  action".
