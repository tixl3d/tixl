# MidiPitchbendOutput

*in [Lib.io.midi](README.md)*

Send 14-bit pitchbend events to the selected Device on the given Channel. The value can be provided either as an integer value between -8192 and 8191 or as a normalized float value between -1.0 and 1.0.

Values can either be sent continuously (one value per frame) or only when the trigger input is sent a (temporary) boolean "true" value.
Sending continuously can lead to flooding of the receiving device, so if possible, use the trigger option.

You can, for instance, use an [AnimValue]'s "WasHit" output and set it to a frequency that is sufficiently smooth for your purpose to reduce the load.

This node can be used in combination with [MidiNoteOutput], [MidiControlOutput], and [MidiTriggerOutput] to create generative music or control hardware. See the [MidiNoteOutputExample]

AKA: pitch, pitchbend, transpose, detune

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SendMode** (Int32) | — |
| **TriggerSend** (Boolean) | — |
| **Device** (String) | — |
| **ChannelNumber** (Int32) | — |
| **Pitch** (Int32) | — |
| **UsePitchFloat** (Boolean) | — |
| **PitchFloat** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

