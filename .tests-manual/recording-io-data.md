---
id: recording-io-data
title: Recording — IO data (MIDI / OSC) capture and replay
scope: timeline
tags: [hardware, essential]
prerequisites:
  - A project is open with the composition's Syncing mode set to **Timeline**.
  - A MIDI input device is connected (controller, keyboard, surface, etc.).
  - Optional: an OSC source configured (a phone app, TouchOSC, custom client).
related-help:
  - ../.help/docs/using/Recording.md
---

End-to-end verification of the IO data half of the recording feature: MIDI and OSC events
during the record window are captured into a `.data` file, automatically wired into a
`LoadDataClip` op on the timeline, and replayable through `SimulateIoData` so downstream
`MidiInput` / `OscInput` ops fire identically to the live capture — even without the
original device connected.

## Step: Record MIDI controller activity

**Action:**
Click the record button. Wiggle a few CC knobs / play a few notes on your MIDI
controller for ~10 seconds. Stop.

**Expected:**
- During recording: a **LoadDataClip** op appears on a fresh layer one below the audio
  clip (data on the lower row, audio on the upper).
- The data clip's body shows per-event **tick marks** as events arrive (sparse mode), or
  a soft fill at very high event density.
- After stop: the clip's label changes to the new filename (e.g. `rec-007`).

## Step: Inspect the recorded `.data` file

**Action:**
Open `<project>/Assets/dataclips/rec-NNN.data` in a text editor.

**Expected:**
- Top-level keys: `Version`, `Metadata` (with `TixlVersion` + `RecordedAtUtc`), `Channels`.
- One `Channel` entry per (device, MIDI channel, controller / note) tuple you exercised.
- Each channel has `Path` (array starting with `"Midi"`), `Type: "float"`,
  `DurationType` (`"Tick"` for CCs, `"Interval"` for notes), and an `Events` array.
- CC events: `{ "Time": <secs>, "Value": <0–127> }`.
- Note events: `{ "Time": <secs>, "EndTime": <secs>, "Value": <velocity> }`.

## Step: Replay the recording — chain with SimulateIoData

**Action:**
With the recording's `LoadDataClip` on the timeline, drop a **SimulateIoData** op into
the graph. Wire `LoadDataClip.Clip → SimulateIoData.Clips`. Connect
`SimulateIoData.Execute` into the parent's Execute. Move the playhead before the clip
and press play.

**Expected:**
- Existing `MidiInput` ops in the graph configured to the recorded device fire
  identically to the live capture — same device name match, same channel, same
  controller / note number, same value progression.
- Toggling `SimulateIoData.Enabled` to false stops the replay events; setting it back to
  true resumes from the current playhead position (no replay of the events you skipped).

## Step: Replay without the original device connected

**Action:**
Disconnect the MIDI controller. Restart the editor (so the device isn't enumerated). Open
the project. Hit play.

**Expected:**
- The `MidiInput` ops still receive the simulated events from `SimulateIoData`. The
  recording drives them via the **SimulatedIoBus** path, which doesn't require the
  hardware to be present.
- The Console shows no warnings about the missing device.

## Step: OSC capture and replay

**Action:**
With an OSC client sending to the port configured in `CoreSettings.DefaultOscPort`, click
record, send a few messages (e.g. `/foo/bar` with float values), stop. Connect an
`OscInput` op bound to the same port to a visible parameter. Hit play.

**Expected:**
- The `.data` file contains channels with `Path[0] == "OSC:<port>"` and the address
  segments following.
- During playback, `OscInput` receives the simulated messages identically and drives the
  parameter.

## Step: Multiple LoadDataClips drive one SimulateIoData

**Action:**
Drop the same `.data` file onto the graph twice (or two different `.data` files).
Connect both `Clip` outputs into the same `SimulateIoData.Clips` multi-input. Hit play.

**Expected:**
- Both clips dispatch independently — each has its own source-time cursor.
- Events from both clips fire through the bus simultaneously when their TimeRanges
  overlap with the playhead.

## Step: Backward scrub

**Action:**
While playback is running through the clip, drag the playhead backward over the clip.

**Expected:**
- No events fire on the backward jump (the cursor snaps without dispatching).
- Forward playback after the scrub resumes dispatching from the new cursor position.
- Held MIDI notes are **not** auto-released (known limitation; not a regression).
