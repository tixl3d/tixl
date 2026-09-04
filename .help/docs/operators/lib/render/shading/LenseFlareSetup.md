# LenseFlareSetup

*in [Lib.render.shading](README.md)*

Pre-made complex light flare setups with various styles.

Require at least one [PointLight] on the left side or within the same group / graph.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Brightness** (Single) | Controls the brightness of the Lensflare in relation to the intensity of the light source. |
| **RandomSeed** (Int32) | Automatically creates different combinations / styles of LensFlares.<br/><br/>Elements include: Centerglow, Star, Color, Sprites, Iris, Sparkle and Shimmer settings. |
| **LightIndex** (Int32) | Select the Index / ID of the Pointlight to which the Lensflare is attached. |
| **RandomizeColor** (Vector4) | Select a color to tint various elements. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

