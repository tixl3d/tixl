---
id: soundtrack-native-audio
title: Soundtrack Playback on the Native Audio Engine
scope: audio
tags: [audio]
added: 2026-11-02
added-in-version: 4.4
prerequisites:
  - A project with a soundtrack in MP3, WAV, OGG or FLAC, ideally a click track.
  - The audio output is audible.
---

Covers soundtrack playback, sync, analysis, images and export after moving away from BASS.

## Step: Playing the soundtrack

**Action:**
Open the project and press play.

**Expected:**
- The soundtrack plays cleanly, without crackling.
- A click track stays in sync with a visual on the beat.

## Step: Seeking

**Action:**
While playing, click at several positions in the timeline, forward and backward.

**Expected:**
- Audio continues from the clicked position right away.
- Audio doesn't briefly replay the position it left.

## Step: Pausing and speed

**Action:**
Pause and resume, then play at double speed.

**Expected:**
- Pausing silences the audio, resuming continues at the playhead.
- At double speed the audio plays twice as fast at a higher pitch and stays with the playhead.

## Step: Audio reactions

**Action:**
Play a graph with `[AudioReaction]` and `[AudioWaveform]`, and compare it with the same project in the last release.

**Expected:**
- Reactions follow the music with the same strength as in the last release.
- The waveform shows both channels moving.

## Step: Clip images

**Action:**
Close TiXL, delete the `SoundtrackImages` folder from the cache directory, reopen the project, and try each clip image style.

**Expected:**
- Spectrum, waveform and volume images are regenerated and look like before.

## Step: Video export

**Action:**
Export a few seconds with audio from a range that doesn't start at 0, and export the same range again. Press play afterwards.

**Expected:**
- The exported audio starts at the right position and is in sync with the picture.
- Both exports sound identical.
- Reactions in the picture follow the music.
- Live playback works again after the export.

## Step: Exporting a clip that isn't the main soundtrack

**Action:**
Add an `[AudioClip]` with **AutoPlay** on and **Display** set to **Clip**, plus `[AudioWaveform]` driving something visible. Export a video from inside the clip's range.

**Expected:**
- The clip is audible in the exported video, as it is in live playback.
- The visuals react to it in the exported video.
