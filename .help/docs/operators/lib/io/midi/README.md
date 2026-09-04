# Lib.io.midi

## Operators

- [**LinkToMidiTime**](LinkToMidiTime.md) — This helper uses MIDI time clock events to drive the playback time. This can be useful for live performances when you want to sync visuals to a clock provided by a DAW like Ableton or Traktor. Tip: When connecting the SyncTrigger parameter to a [MidiInput] and [HasValueIncreased], be sure to disable damping on the MidiInput.
- [**MidiClip**](MidiClip.md) — Creates a time clip bar within the DopeView of the Timeline, similar to how video editing apps show clips.
- [**MidiControlOutput**](MidiControlOutput.md) — Send 7 bit ControlChange or Channel Pressure events to the selected device. The value is given either as an integer value between 0 and 127 or as a normalized float value between 0.0 and 1.0.
- [**MidiInput**](MidiInput.md) — Provides input from connected MIDI devices. By default, the MIDI range from 0 to 127 is mapped to an output range from 0 to 1, which can be adjusted with the parameters.
- [**MidiNoteOutput**](MidiNoteOutput.md) — Send MIDI notes to the selected device on the given channel. The velocity is provided either as an integer between 0 and 127 or as a normalized float value between 0.0 and 1.0
- [**MidiOutput**](MidiOutput.md) — Send notes or controller change events to the selected device. This operator is experimental. The velocity is given as a normalized float value between 0 and 1.
- [**MidiPitchbendOutput**](MidiPitchbendOutput.md) — Send 14-bit pitchbend events to the selected Device on the given Channel. The value can be provided either as an integer value between -8192 and 8191 or as a normalized float value between -1.0 and 1.0.
- [**MidiRecording**](MidiRecording.md) — An experimental implementation to record MIDI input signals.
- [**MidiSysexOutput**](MidiSysexOutput.md) — Allow sending midi sysex string to a selected midi device.
- [**MidiTriggerOutput**](MidiTriggerOutput.md) — Send ProgramChange or Sequencer commands to the selected Device on the chosen Channel. The ProgramChangeNumber is given as an integer between 0 and 127.

---

*Auto-generated from the operator library.*
