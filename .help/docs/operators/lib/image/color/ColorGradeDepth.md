# ColorGradeDepth

*in [Lib.image.color](README.md)*

An advanced color grade that uses a depth buffer-based gradient look for additional effects.

Useful combinations: [SetFog]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **PreSaturate** (Single) | — |
| **Gain** (Vector4) | — |
| **Gamma** (Vector4) | — |
| **Lift** (Vector4) | — |
| **VignetteColor** (Vector4) | — |
| **VignetteRadius** (Single) | — |
| **VignetteFeather** (Single) | — |
| **VignetteCenter** (Vector2) | — |
| **DepthBuffer** (Texture2D Required) | — |
| **Gradient** (Gradient) | — |
| **GradientDepthRange** (Vector2) | — |
| **CamNearFarClip** (Vector2) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

