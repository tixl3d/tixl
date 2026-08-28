---
id: bpm-change-media-clips
title: BPM Change — audio and video clips keep their content timing
scope: timeline
tags: [user, essential, edge]
added: 2026-08-21
added-in-version: 4.3
prerequisites:
  - A project is open with its own Composition Settings enabled, Project Setup set to Animation, BPM at 120.
  - A short audio file (10–30s) and a short video file (10–30s, with a visually recognizable beginning) are available on disk.
related-help:
  - ../.help/docs/using/Timeline.md
  - ../.help/references/topics/ui/ProjectSettings.md
---

All timeline placement in TiXL is in bars, but audio and video content runs in seconds. Changing
the project BPM must not silently move *which part of the file* a clip plays. This set trims audio
and video clips, adds keyframes to them, changes the BPM, and checks that the content and the
keyframes stay where they were authored — while op-backed clips that remap nested compositions
keep following the bars as before.

## Step: Audio trim survives a BPM change

**Action:**
Drop the audio file onto the clip area and trim its start handle so playback through the clip
starts about 2 seconds into the file (check **Source Offset** in the Parameter Window). Play across
the clip and note which moment of the audio you hear first. Open Project Settings → Timing, set
**BPM** to 90, and play across the clip again.

**Expected:**
- At 90 BPM the clip block still occupies the same bar range as before.
- Playback still starts at the same moment of the audio (about 2 seconds in) — the trim did not
  slide to a later part of the file.
- The Parameter Window still shows a **Source Offset** of about 2 seconds.
- Because the block now spans more real seconds than the audio window it plays, the sound ends
  before the block does; there is no speed or pitch change.

## Step: Audio clip shows the same speed percentage as a video clip would

**Action:**
With the BPM still at 90, look at the trimmed audio clip's label and hover it for the tooltip.
Then right-click the clip and choose **Clear Time Stretch**.

**Expected:**
- The clip label carries a "(75.0%)" suffix and the tooltip a "Speed: 75.0%" line — the same
  mapping readout as for a video clip — even though audio keeps its native pitch and rate.
- After **Clear Time Stretch** the suffix disappears, the source window grows to cover the whole
  clip block (the sound now lasts until the block ends), and the start trim is unchanged.

## Step: Video trim survives a BPM change

**Action:**
Set the BPM back to 120. Drop the video file onto the clip area and trim its start handle so the
clip begins about 2 seconds into the footage (the start thumbnail changes to the later frame).
Note the first frame you see when playing across the clip. Set **BPM** to 90 and play across
the clip again.

**Expected:**
- The clip block occupies the same bar range as before.
- The first frame shown is the same frame as at 120 BPM (2 seconds into the footage); the start
  thumbnail on the clip is unchanged.
- The footage plays slower so that the same stretch of video fills the (now longer) clip.
- The clip label gains a speed suffix of about "(75.0%)" and the tooltip shows "Speed: 75.0%".

## Step: Keyframes on a clip's own parameter stay with the content

**Action:**
With BPM at 120, select the video clip's op in the graph and, in the Parameter Window, set two
keyframes on its **Color** alpha (fully transparent at the clip start, fully opaque one second
into the clip) so the footage fades in. Note the frame of the footage at which the fade
completes. Set **BPM** to 90 and scrub across the clip start.

**Expected:**
- The fade still completes on the same frame of the footage as before.
- In the dope sheet the keyframes still sit at the same position relative to the clip block
  (at its start and a fraction of its width in).
- Repeat with an audio clip and an animated **Volume**: the volume ramp still completes at the
  same moment of the audio.

## Step: Op-backed time clips still follow the bars

**Action:**
Add an operator-driven time clip (e.g. select a few animated ops and use **Combine as Time
Clip**) containing keyframed animation, placed at bars 4–8 with its own keyframes inside. Set
**BPM** from 120 to 90 and back to 120 while watching its keyframes and the rendered output.

**Expected:**
- The clip block and its inner keyframes stay on the same bars (their real-time position shifts
  with the BPM, like all bar-based animation).
- No speed suffix appears on the clip label when the BPM changes (its source range is in bars).
- The animation inside still starts exactly at the clip start on both BPM values.

## Step: Main soundtrack re-sizes to the full file at the new BPM

**Action:**
Right-click the audio clip and choose **Set as Main Soundtrack**. Set **BPM** to 90 and then 140.

**Expected:**
- The waveform behind the timeline re-sizes each time so it still spans the file's full length
  at the current BPM (longer in bars at 140, shorter at 90).
- Playback stays in sync with the waveform at every BPM; the audio always starts at the file's
  first sample.

## Step: "On BPM Change: Keep Seconds" keeps clips and keyframes at their seconds

**Action:**
With BPM at 120, note the bar position of the video clip, of a keyframe on a non-clip op (e.g. an
animated [Transform]) and the loop range. In Project Settings → Timing set **On BPM Change** to
**Keep Seconds**, then drag **BPM** to 60 and watch the timeline while dragging. Switch
**Timeline Display** to Secs to compare.

**Expected:**
- While dragging, the clips, the keyframes and the loop range move live: at 60 BPM they sit at
  half the bar numbers, but at exactly the same seconds as before.
- The playhead also stays at the same second.
- The video clip's speed suffix is gone — it plays at 100% in both BPMs (position, trim and
  speed are all unchanged in seconds).
- Keyframes inside a nested composition (op-backed time clip) are *not* rescaled — they stay on
  their bars relative to their clip.
- Pressing Ctrl+Z once restores BPM 120 and every position exactly; Ctrl+Y re-applies.
- With **Stretch with Beat** selected instead, the same BPM drag leaves everything on its bars (the
  behavior of the earlier steps).

## Step: Restoring the original BPM restores the original extents

**Action:**
With the clips from the steps above in place, set **BPM** back to 120.

**Expected:**
- Every clip's bar extent, trim, speed suffix (or its absence) and keyframe positions read
  exactly as they did before the BPM edits.

## Step: Project saved before this change loads with converted source ranges

**Action:**
Open a project containing trimmed audio or video clips (ideally with keyframes on a clip's own
parameters) that was saved with an earlier TiXL build, before the source unit existed. Do not
touch the BPM. Play across the clips, then open **Edit Clip Times...** on one of them.

**Expected:**
- Every clip plays exactly as it did before: same start position, same trim, same speed, and
  keyframes still land on the same moment of the content.
- The **Source** row of the media clip now reads in seconds (its old bar values were converted
  with the project's BPM); op-backed clips still read in bars.
- The console shows a "Converted source range ... to seconds" line per media clip on load.
- After saving, closing and reopening, the clips still play the same and no conversion line
  appears again.

## Step: Clip timing editor shows media source ranges in seconds

**Action:**
Select the trimmed video clip, right-click it and choose **Edit Clip Times...**. Compare the
**Clip** row with the **Source** row. Then do the same for an op-backed time clip.

**Expected:**
- For the video clip the **Source** start/duration/end read in seconds (the trim of about 2 s
  shows as `2.00`), while the **Clip** row reads in bars.
- The **Speed** field shows the real playback rate (100% at 120 BPM for an untrimmed drop).
- For the op-backed time clip both rows read in bars, as before.
