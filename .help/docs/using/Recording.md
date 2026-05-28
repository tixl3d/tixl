# Recording Live Sessions

TiXL can record a live performance — audio plus the MIDI and OSC events that drove the visuals — and replay it later. This is useful for two things:

- **Tweaking after the fact.** Adjust audio-reactive thresholds, MIDI mappings, and animations against the exact session you played, without needing the original controller plugged in.
- **Preparing the next show.** Run the captured performance back through the graph as you iterate on visuals, so you're designing against real input instead of dummy values.

A single click on the toolbar's **record button** captures both audio and IO data together. On stop, two clips appear on the timeline: a `TimelineAudioClip` with the recorded `.wav` and a `LoadDataClip` op with the recorded `.data`. Both files land in the active project's `Assets/` folder and show up in the AssetLib.

## Quick start

1. Set the composition's **Syncing** mode to *Timeline* (Project Settings → Playback). Recording is disabled in Tapping mode because clips need a coherent playhead position.
2. Click the **record button** on the right end of the timeline toolbar.
3. Perform — play notes, twist knobs, send OSC, talk into the mic, whatever you'd do in a live set.
4. Click record again to stop.

Two clips appear on the timeline at the bar you started recording. Hit play to replay the session.

## What gets captured

| Source | File | Notes |
|---|---|---|
| Audio | `.wav` (16-bit PCM, native sample rate) | The configured WASAPI input device, or the system default if none is set. Recording works in any **AudioSource** mode — switching to ProjectSoundtrack doesn't disable mic capture. |
| MIDI | `.data` (JSON) | Every MIDI device currently registered with TiXL. Notes, CCs, pitch bend, channel pressure. |
| OSC | `.data` (same file) | The port configured in `CoreSettings.DefaultOscPort`. |
| Variations / Snapshot recalls | — | **Not captured.** Recalls depend on the active composition's state, which changes over a live session; replaying them in a different context wouldn't reproduce the same result. |

## File layout

```
<project>/
  Assets/
    audio/
      rec-007.wav
    dataclips/
      rec-007.data
```

The session index (`007`) is shared between audio and data within one recording so they pair up. A backup copy of each file also lands in `%APPDATA%\TiXL<version>\Recordings\` — useful if a project file gets damaged.

## Replaying

The `LoadDataClip` op exposes the recorded data as a `DataClip` value on the graph. To drive `MidiInput` / `OscInput` ops from the recording, chain it through a **`SimulateIoData`** op:

```
LoadDataClip(rec-007.data).Clip ─► SimulateIoData.Clips ─► Execute
```

`SimulateIoData` dispatches recorded events through a parallel bus the input ops subscribe to. Replay works regardless of whether the original device is currently connected — `MidiInput` ops match by device name string, not by hardware presence.

Multiple `LoadDataClip`s can feed one `SimulateIoData` via its multi-input. Each clip tracks its own source-time cursor; events from overlapping clips fire simultaneously.

The audio side just plays — drop the `TimelineAudioClip` on the timeline and the audio engine renders it like any other clip.

## Tweak / re-record loop

The typical workflow:

1. Record a session.
2. Hit play, watch the visuals, see what doesn't react the way you wanted.
3. Adjust an `[AudioReaction]` threshold, a `MidiInput` Range, a parameter curve.
4. Loop back to play and re-evaluate against the same captured session.
5. When the visuals are tuned, the next live show should behave the same way against fresh live input.

The recording's `LoadDataClip` is a normal `[TimeClip]` — you can move it on the timeline, trim its start/end via the source-range handles, or even drop the same `.data` file twice for repeated playback.

## Editing recorded data

The on-disk `.data` format is JSON, designed to be human-readable and tool-friendly:

```json
{
  "Version": 1,
  "Metadata": {
    "TixlVersion": "4.5",
    "RecordedAtUtc": "2026-05-28T16:42:11.0123456Z"
  },
  "Channels": [
    {
      "Path": ["Midi", "APC mini mk2", "Ch1", "CC49"],
      "Type": "float",
      "DurationType": "Tick",
      "Events": [
        { "Time": 0.301, "Value": 70.0 },
        ...
      ]
    }
  ]
}
```

You can edit the file in a text editor to trim or modify events. After saving, the `LoadDataClip` op picks up the change automatically (file-watch).

## Limitations

- **Variation / snapshot replay** isn't supported — the active composition's state changes too much across sessions for a reliable round-trip.
- **Hung notes on scrubbing.** During edit-time scrub backward over an active note, the note's `NoteOff` isn't fired. Move the playhead past the note end to release.
- **Multi-arg OSC messages** are recorded as separate channels per arg index. Replay sends them as single-arg messages.
