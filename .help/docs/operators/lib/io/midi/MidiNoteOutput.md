# MidiNoteOutput

*in [Lib.io.midi](README.md)*

Send MIDI notes to the selected device on the given channel. The velocity is provided either as an integer between 0 and 127 or as a normalized float value between 0.0 and 1.0

When NoteWhileTriggered is selected, a note is played while the TriggerSend input is boolean "true". When it turns "false" a NoteOff command is sent to the receiving device.
When NoteFixedDuration is selected, a note starts when the TriggerSend input is boolean "true" and ends after the time in DurationInSecs has passed or if another new note is triggered (to prevent overlapping or hanging notes).

This node can be used in combination with [MidiControlOutput], [MidiPitchbendOutput] and [MidiTriggerOutput] to create generative music or control hardware.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TriggerSend** (Boolean) | — |
| **SendMode** (Int32) | — |
| **Device** (String) | — |
| **ChannelNumber** (Int32) | — |
| **NoteNumber** (Int32) | — |
| **Velocity** (Int32) | — |
| **UseVelocityFloat** (Boolean) | — |
| **VelocityFloat** (Single) | — |
| **DurationInSecs** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

