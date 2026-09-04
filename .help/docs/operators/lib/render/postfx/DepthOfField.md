# DepthOfField

*in [Lib.render.postfx](README.md)*

Adds a depth of field effect.

Hint: Use a [Locator] and its DistanceToCamera output to use automatic focus distance.

Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAberration] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TextureBuffer** (Texture2D Required) | — |
| **DepthBuffer** (Texture2D Required) | — |
| **Amount** (Single) | Controls the intensity of the blur.<br/><br/>Areas that are further away from the focus point are proportionally more blurred. |
| **FocusDistance** (Single) | Controls how far away from the camera the area is which is not affected by the blur.<br/><br/>ProTip: Use a higher "Amount" to make it easier to identify the area of your scene that are in focus. Or use a [Locator] to have a visual helper to define the FocusPoint. |
| **MaxSamples** (Int32) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |
| **NearFarRange** (Vector2) | Defines the near and far range of the scene which is affected by the effect. |

## Outputs
| Name | Type |
|---|---|
| **TextureOut** | T3.Core.DataTypes.Texture2D |

