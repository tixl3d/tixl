# SpatialAudioPlayerGizmo

*in [Lib.io.audio](README.md)*

Debug visualization gizmo for spatial audio. Shows source/listener locators with direction indicators, min/max distance spheres, and directional cones

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ContextKey** (String) | — |
| **SourcePosition** (Vector3) | Position of the audio source |
| **SourceRotation** (Vector3) | Rotation of the audio source (for directional cone) |
| **ListenerPosition** (Vector3) | Position of the listener |
| **ListenerRotation** (Vector3) | Rotation/orientation of the listener |
| **MinDistance** (Single) | Minimum distance for audio falloff |
| **MaxDistance** (Single) | Maximum distance for audio falloff |
| **InnerConeAngle** (Single) | Inner cone angle in degrees (full volume zone) |
| **OuterConeAngle** (Single) | Outer cone angle in degrees (attenuated volume zone) |
| **ConeLength** (Single) | Length of the cone visualization |
| **Color** (Vector4) | Color for the gizmo visualization |
| **Visibility** (GizmoVisibility) | Controls gizmo visibility |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

