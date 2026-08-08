---
id: recording-io-data
title: Recording — IO data (MIDI / OSC) capture and replay
added: 2026-05-29
added-in-version: 4.2
scope: timeline
tags: [user, hardware, essential]
prerequisites:
  - A project is open with the composition's Syncing mode set to **Timeline**.
  - A MIDI input device is connected (controller, keyboard, surface, etc.).
  - Optional: an OSC source configured (a phone app, TouchOSC, custom client).
related-help:
  - ../.help/docs/using/Recording.md
---

As well as audio, a recording can capture the live control data you send while it runs —
MIDI from a controller and OSC from a phone or app. Those incoming moves are saved as a
data clip on the [ui:Timeline|timeline]. Later you can replay that take with a [SimulateIoData] op, and
your project reacts exactly as it did live — even if the original controller or phone
isn't connected anymore.

## Step: Record MIDI controller activity

**Action:**
Click the record button. Wiggle a few CC knobs / play a few notes on your MIDI
controller for ~10 seconds. Stop.

**Expected:**
- During recording: a **[LoadDataClip]** operator appears on a fresh row just below the
  audio clip (data on the lower row, audio on the upper).
- The data clip's body shows a **tick mark** for each incoming event as it arrives, or a
  soft fill when events come in very fast.
- After stop: the clip's label changes to the new recording's name (e.g. `rec-007`).

## Step: The recorded controls play back

**Action:**
Open the [ui:OutputWindow|Output Window] and pin the recording's [LoadDataClip] `Clip` output to preview it.
Move the playhead through the clip.

**Expected:**
- The preview shows one row per knob, note, or message you moved during the take — every
  control you touched is there, and nothing you didn't.
- Knob (CC) moves show as a line of ticks following the timeline; held notes show as bars
  spanning from when the note started to when it ended.
- A playhead line tracks the timeline's position across the data, and the events sit where
  they happened in the take.

## Step: Replay the recording — chain with SimulateIoData

**Action:**
With the recording's [LoadDataClip] on the timeline, drop a **[SimulateIoData]** op into
the graph. Wire `LoadDataClip.Clip → SimulateIoData.Clips`. Connect
`SimulateIoData.Execute` into the parent's Execute. Move the playhead before the clip
and press play.

**Expected:**
- Any [MidiInput] ops set to the recorded controller respond exactly as they did live —
  same channel, same knob / note, same movement — driven by the recording.
- Toggling `SimulateIoData.Enabled` off stops the replayed moves; turning it back on
  resumes from where the playhead is now (it doesn't replay the part you skipped).

## Step: Replay without the original device connected

**Action:**
Disconnect the MIDI controller. Restart the editor (so the device isn't enumerated). Open
the project. Hit play.

**Expected:**
- The [MidiInput] ops still respond, driven by the recording instead of the controller —
  the hardware doesn't need to be connected for the replay to work.
- No warnings appear about the missing device.

## Step: OSC capture and replay

**Action:**
With an OSC app sending to your configured OSC port, click record, send a few messages
(e.g. `/foo/bar` with some values), stop. Connect an [OscInput] op bound to the same port
and address to a visible parameter. Hit play.

**Expected:**
- The data clip preview shows a row for each OSC address you sent to.
- During playback, [OscInput] receives the replayed messages just as it did live and
  drives the parameter.

## Step: Multiple LoadDataClips drive one SimulateIoData

**Action:**
Drop the same recording onto the graph twice (or two different recordings).
Connect both `Clip` outputs into the same `SimulateIoData.Clips` input. Hit play.

**Expected:**
- Both clips play back independently — each follows its own position in the take.
- Where the two clips overlap the playhead at the same time, the moves from both replay
  together.

## Step: Backward scrub

**Action:**
While playback is running through the clip, drag the playhead backward over the clip.

**Expected:**
- Nothing replays on the backward jump — the position just moves without firing events.
- Playing forward again after the scrub resumes replaying from the new position.
- A MIDI note that was being held is **not** automatically released (known limitation).

## Step: Cutting a clip preserves event timing on both halves

**Action:**
With the data clip selected and [SimulateIoData] still wired in, position the playhead somewhere inside the clip and pick **Cut at Time** from the clip context menu (or use the shortcut). Move the playhead through both halves and watch how the recording replays.

**Expected:**
- The data clip splits into two clips, both still playing back the same recording.
- The **left** clip's body shows only the events before the cut; the **right** clip shows the events after the cut. No events vanish or shift sideways inside either body.
- Playing across the split replays the recorded moves in the same order and at the same moments as before the cut — crossing the split is seamless.
- Events that belong to the right half do **not** replay while the playhead is still in the left half.

## Step: Trimming preserves stretch

**Action:**
Stretch the data clip first by holding `Alt` and dragging its right edge — the bar at the bottom of the clip turns red and the "(NN%)" speed indicator in the name updates. Now release `Alt` and drag the right edge inward without any modifier.

**Expected:**
- The speed indicator stays at the same percentage during the no-modifier drag — trim alone does not change the stretch ratio.
- Event ticks inside the clip body stay at the same screen positions while the right edge moves; events past the new trim simply stop being visible.
- Dragging the left edge inward (also no modifier) behaves the same — speed stays, the event sitting at the new left edge stays put.

## Step: Clear Time Stretch keeps the trimmed-in start

**Action:**
With the same stretched clip, also trim the **start** inward (no modifier) so the source no longer begins at file-time 0. Right-click → **Clear Time Stretch**.

**Expected:**
- The "(NN%)" indicator returns to (or is removed from) the name — stretch is gone, rate is 1.
- The event that was sitting at the left edge of the clip before the action is still at the same left edge afterwards (the trim is preserved).
- The clip stays selected after the menu action.
