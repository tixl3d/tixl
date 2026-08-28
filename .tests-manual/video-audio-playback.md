---
id: video-audio-playback
title: Video Audio — playback and graph routing
scope: video
tags: [user, essential, hardware]
added: 2026-08-09
added-in-version: 4.3
prerequisites:
  - An empty project is open and audio output is audible.
  - A video file with an audio track is at hand — the bundled sample videos are silent, so use your own clip, ideally speech or music and at least 30 seconds long.
---

Covers audio decoded from a video file: that it plays in sync, follows the
`Volume` parameter, routes through the audio graph like any other source, and
goes quiet in every case where it should — for both `[PlayVideo]` and
`[VideoClip]` on the timeline.

## Step: A video plays its sound

**Action:**
Add a `[PlayVideo]` and set its `Path` to your clip. Wire its `Texture` output
into your render chain so the op is evaluated, and start playback.

**Expected:**
- Sound is audible within about half a second of playback starting.
- Picture and sound stay together — check on a clip with speech, at 10 seconds
  and again at 60 seconds in; lips and voice must not drift apart over that
  minute.

## Step: Audio-reactive ops see the video's sound

**Action:**
Set Project Settings → Timing → Project Setup to **Animation**. Add an
`[AudioReaction]` and play the video with `Volume` at 1.0.

**Expected:**
- `[AudioReaction]` responds to the video's audio with no extra wiring — the FFT
  is taken from the global mixer, which the video's sound feeds through the
  graph.
- Switching Project Setup to **Live Interactive** stops that: in that mode the
  analysis comes from the audio *input device* instead, so the video's sound no
  longer drives it. This is existing behaviour, not a video-audio fault.

## Step: [AudioReaction] can follow one source instead of the mix

**Action:**
Back in **Animation** mode, add a second sound alongside the video — an
`[AudioToneGenerator]` with `Trigger` held on is enough. Wire both the video's
`AudioReference` and the tone into an `[AudioBus]` so both are audible. Now
insert the `[AudioReaction]` between the video and the bus: video
`AudioReference` → `[AudioReaction]` `Source`, and `[AudioReaction]` `Result` →
the bus.

**Expected:**
- The video still plays, at unchanged level — inserting the op does not alter
  the sound.
- `[AudioReaction]` now responds to the video only. Muting the video (`Volume`
  0) drops its `Level` to 0 even though the tone is still clearly audible.
- Disconnecting `Result` from the bus leaves `Source` connected as a dead-end:
  the operator shows a warning explaining that a side branch can't analyse
  video or clip audio, and `Level` reads 0.
- Removing both connections returns it to analysing the whole mix.

## Step: Volume takes effect, and 0 costs nothing

**Action:**
With the video playing, drag `Volume` from 1.0 down to 0.5, then to 0.0, then
back to 1.0.

**Expected:**
- Loudness follows the slider immediately, with no click or dropout.
- At exactly 0.0 the sound stops completely.
- Returning to 1.0 resumes in sync — the sound picks up at the current playhead
  position, not where it stopped.

## Step: Animated volume follows its keyframes

**Action:**
On a video clip placed at 0–10 seconds, keyframe `Volume`: 0.0 at 0 seconds,
1.0 at 3 seconds, 1.0 at 7 seconds, 0.0 at 10 seconds. Play from 0.

**Expected:**
- A smooth fade up between 0 and 3 seconds, full level between 3 and 7, and a
  smooth fade down between 7 and 10 seconds.
- The fades are continuous — no stepping, clicks, or dropouts at the keyframes.
- Scrubbing to 5 seconds and playing shows full level immediately, not a fade
  restarting from 0.

## Step: Stopping playback stops the sound

**Action:**
Press stop (or set playback speed to 0) while the video is sounding, wait three
seconds, then start playback again.

**Expected:**
- Sound stops within about a tenth of a second of the transport stopping.
- No sound at all while the transport is stopped.
- Starting again resumes in sync at the current playhead.

## Step: Scrubbing and reverse are silent

**Action:**
While playing, drag the time marker back and forth in the timeline. Then set
playback speed to -1, and then to 2.

**Expected:**
- Dragging the marker produces no audio (no chirps, grains, or bursts).
- Reverse playback is silent.
- 2× playback is silent (pitch-correct fast playback is a later milestone).
- Returning to speed 1 and playing forward brings the sound back within about
  half a second.

## Step: Un-evaluating the op silences it

**Action:**
With the video sounding, disconnect `[PlayVideo]`'s `Texture` output from the
render chain. Reconnect it. Then delete the op entirely.

**Expected:**
- Disconnecting silences it within about a tenth of a second — no picture means
  no sound.
- Reconnecting brings the sound back in sync at the current playhead.
- Deleting the op silences it and leaves no stuck sound behind.

## Step: Audio without picture stays silent

**Action:**
Wire only `[PlayVideo]`'s `AudioReference` into an `[AudioBus]`, leaving its
`Texture` output connected to nothing. Play.

**Expected:**
- No sound. Sound follows the picture, so an operator nothing draws is silent —
  this is intended behaviour, not a fault.
- Connecting `Texture` into the render chain starts the sound within about half
  a second.

## Step: Routing through an [AudioBus]

**Action:**
Add an `[AudioBus]` and wire `[PlayVideo]`'s `AudioReference` output into it.
Pin the bus to an output window (or wire its `Result` into the render chain).
Adjust the bus `Volume`.

**Expected:**
- The video's sound continues playing, now through the bus.
- The bus `Volume` scales it, and the bus `Level` output meters it.
- Deleting the connection to the bus leaves the sound playing (it falls back to
  the implicit default bus), unchanged in level.

## Step: Effects and grouping apply

**Action:**
Insert an `[AudioReverb]` between `[PlayVideo]` and the `[AudioBus]`. Set its
`Mix` to about 0.5. Then insert a `[CombineAudio]` in front of the bus with both
the video and an `[AudioToneGenerator]` wired into it, and adjust the
`[CombineAudio]`'s `Volume`.

**Expected:**
- The reverb is audible on the video's sound.
- The `[CombineAudio]` `Volume` scales the video and the tone together.
- Removing the reverb lets its tail ring out rather than cutting it off.

## Step: A silent video file is handled cleanly

**Action:**
Set `[PlayVideo]`'s `Path` to a video with no audio track (the bundled
`test-720p.mp4` has none). Play it.

**Expected:**
- The picture plays normally.
- No warning or error appears on the operator — a video without sound is a
  normal case, not a fault.
- Switching `Path` back to a clip with audio starts its sound within about half
  a second.

## Step: A timeline clip sounds only inside its cut

**Action:**
Drag your clip onto the timeline so it becomes a `[VideoClip]` drawn by a
`[VideoClipPlayer]`. Set its time range to start at 4 seconds and end at 12
seconds of playback time. Play from 0.

**Expected:**
- Silence from 0 to 4 seconds — including the half second before the cut, where
  the player pre-rolls the decoder. No fade-in, no early burst.
- Sound starts at 4 seconds, together with the picture.
- Sound stops at 12 seconds, at the cut, with no tail.

## Step: Trimming the source moves the sound with it

**Action:**
With that clip still at 4–12 seconds, drag its left source trim so the clip
starts 5 seconds into the source file. Play from 4 seconds.

**Expected:**
- At playback 4 seconds you hear the moment 5 seconds into the file — the same
  moment you see.
- Picture and sound stay together for the rest of the clip.

## Step: Two clips hand over cleanly

**Action:**
Place two video clips back to back on the timeline: the first at 0–5 seconds,
the second at 5–10 seconds. Play from 0.

**Expected:**
- Only one clip is audible at a time.
- The handover happens at 5 seconds, within a frame — no overlap where both are
  heard, and no gap longer than a frame.

## Step: Rendered video carries the audio

**Action:**
Place a video clip with speech at 0–10 seconds on the timeline, leaving its
`AudioReference` unwired. Render the range 0–10 seconds to a file, then play the
result in an external player.

**Expected:**
- The rendered file has an audio track, and it is the video's own sound.
- Picture and sound line up: a word spoken at 5 seconds in the editor is at
  5 seconds in the rendered file.
- The sound starts at the clip's cut, not before it.

## Step: Rendered audio survives the graph

**Action:**
Wire the same clip's `AudioReference` through an `[AudioReverb]` into an
`[AudioBus]`, set the bus `Volume` to 0.5, and render the same range again.

**Expected:**
- The rendered file has the reverb on it and is quieter — the graph's gain and
  effects are baked into the render, not bypassed.
- Setting `Volume` to 0 on the clip renders a silent audio track rather than a
  full-level one.

## Step: A render starts clean

**Action:**
With an `[AudioReverb]` in the chain, play in the editor until the sound is
clearly audible, stop, and immediately render a range that begins on silence
(for example a second before the clip's cut).

**Expected:**
- The rendered file starts in silence. No reverb tail, echo, or fragment of what
  was last played in the editor fades out over the first second.

## Step: Auto-collect picks up video clips

**Action:**
Add a `[CombineAudio]` with `Auto Collect Clips` enabled, and wire its output
through an `[AudioReverb]` into an `[AudioBus]`. Put two video clips with sound
on the timeline and leave their `AudioReference` outputs **unconnected**.

**Expected:**
- Both clips are audible through the group: the `[CombineAudio]` `Volume` scales
  them together and the reverb applies to both, with no manual wiring.
- Wiring one clip's `AudioReference` explicitly somewhere else removes it from
  the auto-collected group — an explicit connection wins.
- No routing warnings repeat in the log; the clip is claimed by one collector
  only, not fought over by the group and the implicit bus.

## Step: Two renders of the same range are identical

**Action:**
Render the same range twice to two different files, without changing anything in
between. Compare the two files (byte comparison is fine, or extract the audio
tracks and compare).

**Expected:**
- The audio is identical between the two renders. Export must not depend on
  timing, machine load, or how long the render took.

## Step: Live playback still works after a render

**Action:**
Immediately after a render finishes, press play in the editor.

**Expected:**
- Live audio resumes within about a second, in sync, with no stuck silence and
  no leftover stuttering from the export feeding mode.

## Step: Proxy preview keeps the sound

**Action:**
Generate a proxy for your clip and enable proxy preview in the project
settings. Play the video.

**Expected:**
- The picture comes from the proxy (as before), and the sound still plays —
  proxies carry no audio track, so audio must come from the original file.
- Sync is unchanged from the non-proxy case.
