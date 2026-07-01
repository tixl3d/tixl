---
id: recording-audio
title: Recording — Audio capture end-to-end
added: 2026-05-29
added-in-version: 4.2
scope: timeline
tags: [user, hardware, essential]
prerequisites:
  - A project is open and a composition is active.
  - The composition's Syncing mode is set to **Timeline** (Project Settings → Playback).
  - At least one WASAPI input device is available on the system (mic, loopback, or line-in).
related-help:
  - ../.help/docs/using/Recording.md
---

You can record a live take straight onto the [ui:Timeline|timeline]. The record button on the timeline
toolbar captures both audio and incoming control data at once; this set focuses on the
audio side. Each recording becomes an audio clip on the timeline that you can play back
right away, and the captured sound is saved with your project so it's there next time.

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
Speak into the mic / play some audio for ~5 seconds.

**Expected:**
- The clip body keeps growing smoothly; the editor stays responsive with no lag.
- No error messages appear about the recording failing.

## Step: Stop the recording

**Action:**
Click the record button again.

**Expected:**
- The button stops pulsing and returns to its idle (muted) state.
- The clip's label changes from "(recording…)" to the new recording's name (e.g. `rec-007`).
- The clip body fills with the **waveform** of the captured audio within a few seconds.

## Step: The recording appears in your assets

**Action:**
Open the AssetLib window.

**Expected:**
- A new recording (e.g. `rec-007`) appears in the audio assets.
- The AssetLib refreshes on its own — the new recording shows up without you clicking refresh.

## Step: Playback the recording

**Action:**
Move the playhead back to the start of the recorded clip. Hit play.

**Expected:**
- The recorded audio plays back through your speakers / output.
- Scrubbing within the clip keeps the audio in sync.

## Step: Undo reverts the recording session

**Action:**
With playback stopped, press `Ctrl+Z`.

**Expected:**
- The audio clip disappears from the timeline.
- The matching data clip recorded in the same take (its [LoadDataClip] and its timeline placement) also disappears.
- The recordings themselves stay in your assets — undo removes the clips from the timeline, not the saved recordings.

## Step: Recording while in ProjectSoundtrack audio mode

**Action:**
Set the composition's AudioSource to **ProjectSoundtrack** (Project Settings → Playback). Click record. Speak briefly. Stop.

**Expected:**
- The record button works the same — pulses while active, a clip appears on the timeline.
- Playing the new clip back, you hear your **live mic input**, not the project's soundtrack.
- The soundtrack keeps playing during the recording (visuals that react to the soundtrack keep reacting).

## Step: Pausing playback stops recording

**Action:**
Click record to start a new session. After ~2 seconds, press `Space` to pause playback (instead of clicking the record button).

**Expected:**
- The record button stops pulsing and returns to its idle state immediately.
- The take finishes normally — the new clip's label switches from "(recording…)" to its `rec-NNN` name and the recording appears in your audio assets.
- Undo (`Ctrl+Z`) removes the resulting clip pair just as if you'd stopped by clicking the button.

## Step: Successive recordings reuse the same lane

**Action:**
With playback paused, place the playhead at bar 0. Click record, record ~2 seconds, stop. Move the playhead to a later bar where neither of the previous recording's two rows holds another clip at that position. Click record again, record ~2 seconds, stop.

**Expected:**
- Both recordings land on the **same two rows** — the data clip and the audio clip line up vertically across the two takes.
- Now move the playhead onto a position that's covered by the first take. Record again — this third pair lands on the next free pair of rows *above* the first take, not on top of it.

## Step: A new recording appears where you're looking

**Action:**
Scroll / pan the graph canvas far away so none of the existing recording operators are visible. Click record briefly. Stop.

**Expected:**
- The new recording's [LoadDataClip] operator shows up inside the part of the graph you're currently looking at — not somewhere off-screen with the earlier recordings.
- It appears right away, without you needing to click the graph background to refresh.

## Step: Per-project Recording toggles

**Action:**
Open **Project Settings → Recording**. Verify the panel exposes "Capture Audio", "Capture IO", and (when IO is on) the indented "MIDI" / "OSC" sub-checkboxes. Now run three short recordings, changing the toggles between takes:
1. Both on (defaults). Record ~2 s. Stop.
2. Capture Audio off, Capture IO on. Record ~2 s. Stop.
3. Capture Audio on, Capture IO off. Record ~2 s. Stop.

**Expected:**
- Take 1: a paired audio clip + data clip appear, both finish normally.
- Take 2: **only** a data clip appears; no audio clip row, and no audio recording is created.
- Take 3: **only** an audio clip appears (on the chosen row); no data clip, and no data recording is created.
- Turning both off and clicking Record shows a brief "nothing to record …" notice and creates no clips. The button does not start pulsing.

## Step: Selective MIDI / OSC capture

**Action:**
With Capture IO on, uncheck **MIDI** under it. Send both MIDI events (wiggle a CC) and OSC messages during a short take. Stop. Repeat with **OSC** unchecked (and MIDI re-enabled). To check what was captured, replay each take through a [SimulateIoData] op (see the IO-data test) and watch which inputs respond.

**Expected:**
- MIDI-off take: on replay, only the OSC messages drive their inputs — the MIDI controller activity from that take produces nothing, even though the controller was active.
- OSC-off take: on replay, only the MIDI activity drives its inputs — the OSC messages from that take produce nothing.
- Disabling both while Capture IO is on shows a hint under the checkboxes ("Both MIDI and OSC are off …"), and clicking Record captures no control data.
