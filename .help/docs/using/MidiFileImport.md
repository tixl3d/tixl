# Importing MIDI Files

TiXL can load standard MIDI files (`.mid` / `.midi`) — a track exported from a DAW, a downloaded arrangement, a generated pattern — and use their notes and controller curves to drive visuals, without any hardware attached.

Drop a MIDI file onto the **timeline** and it becomes a `[MidiClip]` clip with the file's real duration. Drop it onto the **graph** to get the operator without timeline placement. Either way the file is converted into the same data-channel format that [live session recording](Recording.md) produces, so everything that works with recorded `.data` clips works with imported MIDI too.

## What's in the clip

Each note and controller in the file becomes one channel:

| MIDI content | Channel path | Events |
|---|---|---|
| Notes | `Midi / <file name> / Ch<n> / N<note>` | One interval per note, spanning NoteOn→NoteOff, value = velocity (0–127) |
| Control changes | `… / CC<controller>` | One event per change, value = raw CC (0–127) |
| Pitch bend | `… / PB` | Raw pitch value |
| Channel pressure | `… / CP` | Raw pressure (0–127) |

Times come from the file's own tempo map (including mid-file tempo changes), so events land where they sound. Values stay in raw MIDI range — remap downstream where needed.

The clip body on the timeline shows the events as tick marks and bars; select the clip to inspect all channels in the details pane.

## Driving parameters

Two sampler ops read channel values directly at the playhead:

- **`[SampleFloatFromDataClip]`** — the value of the last event at or before the current time. Use for CC curves and pitch bend. A dropdown on its `Channel` parameter lists every channel of the connected clip.
- **`[SampleGateFromDataClip]`** — for note channels: `Gate` is true while a note is held, `Velocity` carries its velocity.

Both also expose **`WasHit`**, true for one frame whenever an event starts — the reliable trigger signal even when consecutive events carry identical values, or a note falls entirely between two frames.

```
MidiClip(track.mid).Clip ─► SampleGateFromDataClip(Ch1/N36).Gate ─► [flash on every kick]
```

## Simulating a device

Alternatively, replay the file through the MIDI input system — useful when a project is already wired up with `[MidiInput]` ops for a controller:

```
MidiClip(track.mid).Clip ─► SimulateIoData.Clips ─► Execute
```

`[MidiInput]` ops receive the file's events as if a device named after the file (e.g. `track.mid`) were playing them. Set the op's `Device` parameter to the file name, and channel/controller filters work as with real hardware.

## Notes

- The clip is a normal TimeClip: move, trim, split, or place the same file several times.
- Clip length converts the file's duration at the *project* BPM — a file authored at a different tempo won't end exactly on a bar boundary, same as audio clips.
- Edits to the file on disk are picked up automatically (file-watch).
