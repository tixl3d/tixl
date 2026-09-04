# TimeClip

*in [Lib.flow](README.md)*

Creates a time clip bar within the DopeView of the Timeline, similar to how video editing apps show clips.
TimeClips can be moved by drag and drop and arranged next to and on top of each other (classic NLE non-linear editing).

When the time marker is running over/playing the time clip it is activated and rendered.

It can be helpful to give the time clip a suitable name in the name field (which is "Untitled Instance" by default).

Also see [Switch] for a way to cut between scenes without the bars in the Timeline.

To load an image sequence into the DopeView of the Timeline [ImageSequenceClip] can be used.
[MidiClip] can be used to load linear Midi information into the scene.

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

