# AudioPlayer

*in [Lib.io.audio](README.md)*

An audio player using BASS audio library. Features include:

- [Playback Control]: Independent play, pause, and stop functionality with seek support
- [Stereo Panning]: Position audio in the stereo field from left to right
- [ADSR Envelope]: Optional amplitude envelope for dynamic volume shaping
- [Speed Control]: Playback speed multiplier (affects pitch)

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **AudioFile** (String) | Path to the audio file to play |
| **PlayAudio** (Boolean) | Trigger to start or resume audio playback |
| **StopAudio** (Boolean) | Stops the audio playback and resets the position to the beginning |
| **PauseAudio** (Boolean) | Pauses the audio playback at the current position |
| **Volume** (Single) | The master volume level of the audio (0.0 = silent, 1.0 = full volume, >1.0 = amplified) |
| **Mute** (Boolean) | When enabled, mutes the audio output without stopping playback |
| **Panning** (Single) | Stereo panning position (-1.0 = left, 0.0 = center, 1.0 = right) |
| **Speed** (Single) | Playback speed multiplier (1.0 = normal speed, affects pitch) |
| **Seek** (Single) | Seek to a normalized position in the audio file (0.0 = start, 1.0 = end) |
| **TriggerMode** (Int32) | The trigger behavior mode (e.g., trigger or gate) for envelope playback |
| **Duration** (Single) | Optional duration override for playback (in seconds) |
| **UseEnvelope** (Boolean) | When enabled, applies the Envelope input to modulate amplitude |
| **Envelope** (Vector4) | ADSR envelope applied to amplitude when UseEnvelope is enabled |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |
| **IsPlaying** | System.Boolean |
| **GetLevel** | System.Single |

