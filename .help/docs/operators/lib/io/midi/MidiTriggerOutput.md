# MidiTriggerOutput

*in [Lib.io.midi](README.md)*

Send ProgramChange or Sequencer commands to the selected Device on the chosen Channel. The ProgramChangeNumber is given as an integer between 0 and 127.

All parameters are sent when a trigger is received in the respective input.
It should only be a momentary, one frame long "true" value to not flood the receiving gear.

Be aware that not many applications actually react to these triggers! Hardware is more likely to have them implemented.

This node can be used in combination with [MidiNoteOutput] [MidiControlOutput] and [MidiPitchbendOutput] to create generative music or control hardware.

AKA: programchange, start, stop, continue, tempo, bpm

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Device** (String) | — |
| **ChannelNumber** (Int32) | — |
| **TriggerProgramChange** (Boolean) | — |
| **ProgramChangeNumber** (Int32) | — |
| **TriggerStart** (Boolean) | — |
| **TriggerStop** (Boolean) | — |
| **TriggerContinue** (Boolean) | — |
| **TriggerTempoEvent** (Boolean) | This trigger sends Tool's current BPM value to the receiving device. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

