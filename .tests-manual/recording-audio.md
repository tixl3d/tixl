---
id: recording-audio
title: Recording — Audio capture end-to-end
added: 2026-05-29
added-in-version: 4.2
scope: timeline
tags: [hardware, essential]
prerequisites:
  - A project is open and a composition is active.
  - The composition's Syncing mode is set to **Timeline** (Project Settings → Playback).
  - At least one WASAPI input device is available on the system (mic, loopback, or line-in).
related-help:
  - ../.help/docs/using/Recording.md
---

End-to-end verification of the audio half of the live-session recording feature: the
record button on the timeline toolbar drives both audio and IO capture; this set focuses
on the audio side. Recordings land in the project's `Assets/audio/` folder and appear on
the timeline as `TimelineAudioClip` rows.

## Step: Record button is enabled in Timeline mode

**Action:**
Look at the record button on the right end of the timeline toolbar (next to the image-background tool cluster).

**Expected:**
- The button shows the **Record** glyph in a muted neutral colour.
- Hovering it turns the glyph red and shows the tooltip "Start recording (audio + IO data)".
- If you temporarily switch the composition's Syncing mode to Tapping, the glyph fades; tooltip changes to "Recording is only available in Timeline mode." Switch back to Timeline before continuing.

## Step: Start a recording

**Action:**
With the playhead near the start of the timeline, click the record button.

**Expected:**
- The button starts **pulsing red** (sine-wave alpha).
- Playback starts automatically — the playhead advances at the current BPM.
- A new **TimelineAudioClip** appears on a fresh layer at the playhead's start bar.
- The clip's body shows **"(recording…)"** as its label and grows rightward in real time as the recording proceeds.

## Step: Record some audio

**Action:**
Speak into the mic / play some audio through the loopback source for ~5 seconds.

**Expected:**
- The clip body keeps growing; no UI lag.
- The Console has no warnings about capture failures.

## Step: Stop the recording

**Action:**
Click the record button again.

**Expected:**
- The button stops pulsing and returns to its idle (muted) state.
- The clip's label changes from "(recording…)" to the new filename (e.g. `rec-007`).
- The clip body fills with the **waveform** of the captured audio within a few seconds (BASS analyses the file on a background thread).

## Step: File lands in the project's Assets folder

**Action:**
Open the AssetLib window (or browse `<project>/Assets/audio/` in your file explorer).

**Expected:**
- A new `rec-NNN.wav` file is present in `Assets/audio/`.
- The AssetLib refreshes automatically — the file appears without clicking refresh.
- A backup copy also exists in `%APPDATA%\TiXL<version>\Recordings\` (used as the original write target before the on-stop import).

## Step: Playback the recording

**Action:**
Move the playhead back to the start of the recorded clip. Hit play.

**Expected:**
- The recorded audio plays back through the configured audio output.
- Scrubbing within the clip re-syncs audio cleanly.

## Step: Undo reverts the recording session

**Action:**
With playback stopped, press `Ctrl+Z`.

**Expected:**
- The TimelineAudioClip disappears from the timeline.
- The data clip created during the same session (`LoadDataClip` op + its timeline placement) also disappears.
- The original files in `Assets/audio/` and `Assets/dataclips/` **remain** — undo removes the clips, not the files.

## Step: Recording while in ProjectSoundtrack audio mode

**Action:**
Set the composition's AudioSource to **ProjectSoundtrack** (Project Settings → Playback). Click record. Speak briefly. Stop.

**Expected:**
- The record button works the same — pulses while active, clip appears on timeline.
- The recorded `.wav` contains the live mic input, **not** the project's soundtrack.
- The soundtrack keeps playing during the recording (visual reactions to the soundtrack continue).
- WASAPI capture comes up on demand for the recording even though AudioSource isn't ExternalDevice.

## Step: Pausing playback stops recording

**Action:**
Click record to start a new session. After ~2 seconds, press `Space` to pause playback (instead of clicking the record button).

**Expected:**
- The record button stops pulsing and returns to its idle state on the same frame.
- The audio and data files finalise normally — the new clip's label switches from "(recording…)" to the assigned `rec-NNN` name and the `.wav` lands in `Assets/audio/`.
- Undo (`Ctrl+Z`) removes the resulting clip pair as if the session had been stopped by clicking the button.

## Step: Successive recordings reuse the same lane

**Action:**
With playback paused, place the playhead at bar 0. Click record, record ~2 seconds, stop. Move the playhead to a later bar where neither of the previous recording's two rows holds another clip at that position. Click record again, record ~2 seconds, stop.

**Expected:**
- Both recordings land on the **same two layer indices** — data clip and audio clip line up vertically across the two takes.
- Now move the playhead onto a position that's covered by the first take. Record again — this third pair lands on the next free pair *above* the first take, not on top of it.

## Step: New op lands in view when origin is off-screen

**Action:**
Scroll / pan the graph canvas far away from the origin so neither (0, 0) nor any existing `LoadDataClip` op is visible. Click record briefly. Stop.

**Expected:**
- The new `LoadDataClip` op shows up inside the currently visible portion of the graph canvas — not at the origin or stacked under existing recordings off-screen.
- The op appears immediately (without needing to click the graph background to refresh).

## Step: Per-project Recording toggles

**Action:**
Open **Project Settings → Recording**. Verify the panel exposes "Capture Audio", "Capture IO", and (when IO is on) the indented "MIDI" / "OSC" sub-checkboxes. Now run three short recordings, changing the toggles between takes:
1. Both on (defaults). Record ~2 s. Stop.
2. Capture Audio off, Capture IO on. Record ~2 s. Stop.
3. Capture Audio on, Capture IO off. Record ~2 s. Stop.

**Expected:**
- Take 1: a paired `TimelineAudioClip` + `LoadDataClip` row appears, both finalise normally.
- Take 2: **only** a `LoadDataClip` op appears; no `TimelineAudioClip` row, no `.wav` is written. The Console has no warnings about `EndRecording` being called without an active session.
- Take 3: **only** a `TimelineAudioClip` row appears (on the chosen lane); no `LoadDataClip` op, no `.data` file is written.
- Turning both off and clicking Record produces a single Console warning ("nothing to record …") and no clips. The button does not start pulsing.

## Step: Selective MIDI / OSC capture

**Action:**
With Capture IO on, uncheck **MIDI** under it. Send both MIDI events (wiggle a CC) and OSC messages during a short take. Stop. Open the resulting `.data` file. Repeat with **OSC** unchecked (and MIDI re-enabled).

**Expected:**
- MIDI-off take: the `.data` file has only `OSC:…` channels; no `Midi/…` channels are present even though the controller was active during the take.
- OSC-off take: the `.data` file has only `Midi/…` channels; OSC messages sent during the take do not show up.
- Disabling both sub-flags while Capture IO is on shows a hint under the sub-checkboxes ("Both MIDI and OSC are off …") and clicking Record produces no `.data` file.
