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
[LoadMidiFile] clip on the [ui:Timeline|timeline], holding the file's notes and controller
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
- On drop, a **[LoadMidiFile]** clip appears at the drop position with the file's duration.
- The clip can be dragged, trimmed, and split like an audio or video clip.

## Step: Inspect the converted channels

**Action:**
Select the [LoadMidiFile] op and pin its `Clip` output in the [ui:OutputWindow|Output Window].

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
- A [LoadMidiFile] op is created with its `FilePath` set to the imported asset.
