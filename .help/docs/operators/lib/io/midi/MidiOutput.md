# MidiOutput

*in [Lib.io.midi](README.md)*

Send notes or controller change events to the selected device. This operator is experimental. The velocity is given as a normalized float value between 0 and 1.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TriggerSend** (Boolean) | — |
| **SendMode** (Int32) | — |
| **Device** (String) | — |
| **ChannelNumber** (Int32) | — |
| **NoteOrController** (Int32) | — |
| **Velocity** (Single) | — |
| **Velocity127** (Int32) | — |
| **DurationInSecs** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

