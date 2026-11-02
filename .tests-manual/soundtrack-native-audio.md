---
id: soundtrack-native-audio
title: Audio on the Native Engine
scope: audio
tags: [audio]
added: 2026-11-02
added-in-version: 4.4
prerequisites:
  - A project with a soundtrack in MP3, WAV, OGG or FLAC, ideally a click track.
  - The audio output is audible.
---

Covers soundtrack and operator playback, routing, metering, analysis, images and export after moving away from BASS.

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

## Step: Routing a clip through a bus

**Action:**
Wire an `[AudioClip]` into an `[AudioBus]` that is part of the render chain, change the bus **Volume** while playing, then disconnect the clip.

**Expected:**
- The volume follows the bus without clicks, and the audio doesn't jump when the clip is connected or disconnected.
- After disconnecting, the clip plays on at its own volume.
- Stopping playback silences the bus.

## Step: Samples and tones

**Action:**
Wire `[PlayAudioSample]` and `[AudioToneGenerator]` into the bus, and trigger them repeatedly with short pulses and with long gates.

**Expected:**
- Every trigger is heard, starting from the top of the sample.
- The tone follows its envelope and fades out over its release time.
- Sample speed, panning and seek work.

## Step: Level meters

**Action:**
Wire `[AudioLevel]` inline before the bus, then branch a second `[AudioLevel]` off an `[AudioClip]` without wiring it into the bus. Open the audio panel in the settings window.

**Expected:**
- Both `[AudioLevel]` outputs follow the audio and fall to 0 when it stops. The side branch works for clips too and shows no warning.
- `[AudioBus]` **Level** and the operator, soundtrack and global meters move.

## Step: Effects are passed through

**Action:**
Wire a source through `[AudioReverb]`, `[AudioEcho]` and `[AudioCompressor]`.

**Expected:**
- The audio passes through unchanged and each op shows a warning that the effect isn't available.
- Their parameters are still there.

## Step: Video audio

**Action:**
Play a video with audio through `[PlayVideo]` wired into a bus, seek around, and export a few seconds.

**Expected:**
- The audio stays in sync with the picture after seeks.
- The video's audio is in the export, in sync.

## Step: Exporting the audio graph

**Action:**
Export a video with a clip, a triggered sample and a tone wired into a bus. Press play afterwards.

**Expected:**
- All three are audible at the levels heard live, and triggers land where they did live.
- Live playback and all meters work again after the export.

## Step: Reacting to an audio input

**Action:**
In the project settings, set the audio source to the external device and pick a microphone or line input. Make sound while playback runs, with a graph using `[AudioReaction]` and `[AudioWaveform]`. Compare with the last release.

**Expected:**
- The level meter next to the device follows the input.
- Reactions and waveform follow the input with the same strength and decay as in the last release.
- Other apps can use the microphone at the same time.
- Beat locking with onset detection and with the dance model still locks to music.
- Switching the input device doesn't interrupt playing audio.

## Step: Capturing system audio

**Action:**
Pick a loopback entry as the input: on Linux a `Monitor of ...` device, on Windows a device ending in `(Loopback)`. Play music in another app.

**Expected:**
- Reactions follow what the other app plays.

## Step: Recording the input

**Action:**
Record from the timeline toolbar while an input is active, also with the project's own audio selected as the source. Cause a hitch during the recording, for example by opening a large project window. Open the resulting WAV.

**Expected:**
- The WAV is complete, without gaps or clicks at the hitch.
- With the project's own audio as the source, reactions keep following the project during the recording.
