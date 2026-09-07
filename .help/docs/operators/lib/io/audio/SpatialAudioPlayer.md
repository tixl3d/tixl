# SpatialAudioPlayer

*in [Lib.io.audio](README.md)*

A 3D positional audio player using BASS audio library's 3D sound system. It simulates real-world audio by positioning sound sources in 3D space relative to a listener (camera). Features include:

- [Distance Attenuation]: Sound volume decreases with distance between MinDistance (full volume) and MaxDistance (silent)
- [Directional Sound Cones]: Define inner/outer cone angles to create directional audio sources (e.g., speakers, spotlights)
- [Listener Orientation]: Audio panning based on listener position and rotation for realistic spatial perception
- [3D Mode]: Normal (absolute positioning), Relative (source attached to listener for footsteps/breathing), or Off (mono playback)

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **AudioFile** (String) | Path to the audio file to play |
| **PlayAudio** (Boolean) | Trigger to start or resume audio playback |
| **StopAudio** (Boolean) | Stops the audio playback and resets the position to the beginning |
| **PauseAudio** (Boolean) | Pauses the audio playback at the current position |
| **Volume** (Single) | The master volume level of the audio (0.0 = silent, 1.0 = full volume, >1.0 = amplified) |
| **Mute** (Boolean) | When enabled, mutes the audio output without stopping playback |
| **Speed** (Single) | Playback speed multiplier (1.0 = normal speed, affects pitch) |
| **Seek** (Single) | Seek to a specific position in the audio file (in seconds) |
| **SourcePosition** (Vector3) | The 3D position of the audio source in world space |
| **SourceRotation** (Vector3) | The rotation/orientation of the audio source in 3D space (affects directional sound) |
| **MinDistance** (Single) | The distance the audio plays at full volume before attenuation begins |
| **MaxDistance** (Single) | The maximum distance at which the audio can still be heard (beyond this distance audio is silent) |
| **ListenerPosition** (Vector3) | The 3D position of the listener (camera) in world space |
| **ListenerRotation** (Vector3) | The rotation/orientation of the listener (camera) in world space |
| **InnerConeAngle** (Single) | The inner angle of the sound cone (in degrees). Audio plays at full volume inside of this |
| **OuterConeAngle** (Single) | The outer angle of the sound cone (in degrees). Audio will be at OuterConeVolume outside of this |
| **OuterConeVolume** (Single) | The volume level of audio outside the outer cone angle (0.0 = silent, 1.0 = full, >1.0 = amplified) |
| **Audio3DMode** (Int32) | The 3D audio processing mode (e.g., normal, head-relative "footsteps/etc.", or disabled "mono") |
| **Visibility** (GizmoVisibility) | Controls the visibility of the spatial audio gizmo in the editor |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |
| **IsPlaying** | System.Boolean |
| **IsPaused** | System.Boolean |
| **GetLevel** | System.Single |

