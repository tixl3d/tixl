# MidiClip

*in [Lib.io.midi](README.md)*

Creates a time clip bar within the DopeView of the Timeline, similar to how video editing apps show clips.
TimeClips can be moved by drag and drop and arranged next to and on top of each other (classic NLE non-linear editing).

When the time marker is running over / playing the time clip it is activated.

To load an image sequence into the DopeView of the Timeline [ImageSequenceClip] can be used.
[TimeClip] can be used to activate full node trees.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Filename** (String) | Defines the storage location of the midi |
| **PrintLogMessages** (Boolean) | Enables / disables output of the midi information into the Console window |

## Outputs
| Name | Type |
|---|---|
| **Values** | T3.Core.DataTypes.Dict`1[System.Single] |
| **ChannelNames** | System.Collections.Generic.List`1[System.String] |
| **DeltaTicksPerQuarterNote** | System.Single |

