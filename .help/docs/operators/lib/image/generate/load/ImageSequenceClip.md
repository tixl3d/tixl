# ImageSequenceClip

*in [Lib.image.generate.load](README.md)*

Loads an image sequence from a folder and adds it as a time clip bar within the DopeView of the Timeline, similar to how video editing apps show clips.
These clips can be moved by drag and drop and arranged next to and on top of each other (classic NLE non-linear editing).

When the time marker is running over / playing the time clip it is activated and rendered.

To load an Operator tree into the DopeView of the Timeline [TimeClip] can be used.
[MidiClip] can be used to load linear MIDI information into the scene.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **FilePath** (String) | Defines the storage location of the image sequence.<br/> |
| **FrameRate** (Single) | Defines how many images per second are shown |
| **TriggerUpdate** (Boolean) | Forces update if clicked or activated via a bool |
| **Color** (Vector4) | Color value that tints / colors the image sequence |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

