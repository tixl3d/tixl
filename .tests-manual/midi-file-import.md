---
id: midi-file-import
title: MIDI file import — load .mid files as timeline data clips
added: 2026-07-04
added-in-version: 4.2
scope: timeline
tags: [user, assets]
prerequisites:
  - A project is open with the composition's Syncing mode set to **Timeline**.
  - A standard MIDI file (`.mid` / `.midi`) with a few notes and ideally some CC
    automation — any DAW export or downloaded file works.
---

MIDI files can be dropped into a project like audio or video. The file becomes a
[MidiClip] clip on the [ui:Timeline|timeline], holding the file's notes and controller
moves as data channels. Replaying it through a [SimulateIoData] op drives [MidiInput]
operators exactly as if the file were played on a connected device — the file name takes
the place of the device name.

## Step: Drop a MIDI file on the timeline

**Action:**
Drag a `.mid` file from Explorer (or the asset library, after importing) onto the
timeline's clip area.

**Expected:**
- While dragging, a clip-sized preview rectangle follows the mouse; its width matches the
  file's real duration (a 30-second file is wider than a 5-second one), not a fixed size.
- On drop, a **[MidiClip]** clip appears at the drop position with the file's duration.
- The clip can be dragged, trimmed, and split like an audio or video clip.

## Step: Inspect the converted channels

**Action:**
Select the [MidiClip] op and pin its `Clip` output in the [ui:OutputWindow|Output Window].

**Expected:**
- One row per note and controller used in the file. Held notes show as bars spanning
  their duration; CC moves show as tick marks.
- Channel paths read `Midi / <filename> / Ch<n> / N<note>` or `.../CC<controller>`.

## Step: Replay drives MidiInput ops

**Action:**
Wire the clip's output into a [SimulateIoData] op. Add a [MidiInput] op and set its
`Device` parameter to the MIDI file's name (as shown in the channel paths). Set its
Channel/Control to match a note or CC present in the file. Play the timeline through
the clip.

**Expected:**
- The [MidiInput] output follows the file's events — notes trigger while the playhead
  crosses their bars, CC values step through the recorded curve.
- Values arrive in raw MIDI range (velocity / CC 0–127), same as from a live device.
- Scrubbing backwards and replaying works; events fire again on each pass.

## Step: Drop on the graph creates the op too

**Action:**
Drag the same `.mid` file onto the graph background (not the timeline).

**Expected:**
- A [MidiClip] op is created with its `FilePath` set to the imported asset.

## Step: Sample a CC curve directly

**Action:**
Wire the clip's output into a [SampleFloatFromDataClip]. Open its `Channel` parameter —
a dropdown should list all channels of the file. Pick a CC channel (`.../CC<n>`). Play
the timeline through the clip.

**Expected:**
- The dropdown lists the same channels shown in the DataSet view, as `/`-joined paths.
- `Result` steps through the CC curve as the playhead moves (raw 0–127 values), holding
  each value until the next event.
- Before the first event, `Result` returns the `DefaultValue` parameter.
- Enabling `UseTimeOverride` and dragging `OverrideTime` samples the file at that source
  time in seconds, independent of the playhead.

## Step: Note gates trigger

**Action:**
Add a [SampleGateFromDataClip] on the same clip, pick a note channel (`.../N<note>`).
Play through the clip.

**Expected:**
- `Gate` is true exactly while the note's bar is under the playhead in the clip body,
  false between notes.
- `Velocity` carries the note's velocity (0–127) while active, 0 when inactive.
- Picking a CC channel instead shows a warning status on the op ("has no intervals").

## Step: WasHit fires per event

**Action:**
On both sampler ops, pin the `WasHit` output. Pick a channel whose consecutive events
carry the *same* value (repeated notes at one velocity, or a CC that re-sends its value).
Play through the clip.

**Expected:**
- `WasHit` pulses true for one frame at every event start, even when `Result` /
  `Velocity` doesn't change because the values are identical.
- Scrubbing backwards doesn't fire hits; resuming forward playback does.
