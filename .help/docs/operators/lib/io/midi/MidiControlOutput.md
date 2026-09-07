# MidiControlOutput

*in [Lib.io.midi](README.md)*

Send 7 bit ControlChange or Channel Pressure events to the selected device. The value is given either as an integer value between 0 and 127 or as a normalized float value between 0.0 and 1.0.

Values can either be sent continuously (one value per frame) or only when the trigger input is sent a (temporary) boolean "true" value.
Sending continuously can lead to flooding of the receiving device, so if possible, use the trigger option.

You can for instance use an [AnimValue]s "WasHit" output as the trigger and set it to a frequency that is sufficiently smooth for your purpose to reduce the load.

This node can be used in combination with [MidiNoteOutput] [MidiPitchbendOutput] and [MidiTriggerOutput] to create generative music or control hardware. See the [MidiNoteOutputExample]

AKA: aftertouch, pressure, cc, controller

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SendMode** (Int32) | — |
| **TriggerSend** (Boolean) | — |
| **CCorPressure** (Int32) | — |
| **Device** (String) | — |
| **ChannelNumber** (Int32) | — |
| **ControllerNumber** (Int32) | — |
| **Value** (Int32) | — |
| **UseValueFloat** (Boolean) | — |
| **ValueFloat** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

