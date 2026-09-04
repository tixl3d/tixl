# PlayVideo

*in [Lib.io.video](README.md)*

Uses Windows Media Foundation to play a video file. To ensure seek precision while editing, it enforces seeking if timeline playback is paused. If timeline playback is running, it will only seek if the video playback drift exceeds the resync threshold. If this threshold is too small, playback will stutter. If it's excessively large, syncing might be off.

[SetCommandTime] can be used to control / offset the video playback and speed: [PlayVideo] -> [Layer2D] -> [SetCommandTime]

Important: Media Foundation returns textures in BGRA format. This might not work for some draw operators like [Layer2d]. Please insert a [ConvertFormat] operator to convert the format.

Note: If this is flickering, you can try to deactivate Idle Motion from the timeline control bar.



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Path** (String) | Path to the videofile |
| **Volume** (Single) | Defines the volume of the sound output |
| **ResyncThreshold** (Single) | Threshold for timing resyncing |
| **OverrideTimeInSecs** (Single) | Defines whether the video is shifted forwards or backwards in time |
| **Loop** (Boolean) | When activated, the video is repeated endlessly |
| **IsPreciseAtPlayback** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |
| **Duration** | System.Single |
| **HasCompleted** | System.Boolean |
| **UpdateCount** | System.Int32 |

