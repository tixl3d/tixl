# Sharpen

*in [Lib.image.fx.blur](README.md)*

Sharpens the incoming image.

Check out the [ReactionDiffusionExample] for a more advanced use case

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **SampleRadius** (Single) | Defines the radius of the sharpen effect.<br/>For normal image sharpening, small values are recommended (~1-3), larger radii are useful for more creative applications and image stylization. |
| **Strength** (Single) | Defines how strongly the effect is applied. |
| **Clamping** (Boolean) | Helps to get a smooth render for reaction diffusion |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

