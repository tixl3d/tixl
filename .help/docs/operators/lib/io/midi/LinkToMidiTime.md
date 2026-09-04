# LinkToMidiTime

*in [Lib.io.midi](README.md)*

This helper uses MIDI time clock events to drive the playback time. This can be useful for live performances when you want to sync visuals to a clock provided by a DAW like Ableton or Traktor. Tip: When connecting the SyncTrigger parameter to a [MidiInput] and [HasValueIncreased], be sure to disable damping on the MidiInput.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubGraph** (Command) | — |
| **ResyncTrigger** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Commands** | T3.Core.DataTypes.Command |

