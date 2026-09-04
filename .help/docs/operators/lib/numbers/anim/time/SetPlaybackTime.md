# SetPlaybackTime

*in [Lib.numbers.anim.time](README.md)*

An advanced playback control that will move the current playhead to the defined time. 


This can be useful in complex VJ setups. Only use this operator if you know what you're doing. 
Setting the PlaybackTime frequently can interfere with user interactions like editing keyframes. 

Note: For this operator to work, the playback must be running.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubGraph** (Command Required) | — |
| **TimeInBars** (Single) | — |
| **TriggerMode** (Int32) | — |
| **Enabled** (Boolean) | — |
| **ShowLogMessages** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Commands** | T3.Core.DataTypes.Command |

