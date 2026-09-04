# LenseFlareSetupAdvanced

*in [Lib.render.shading](README.md)*

Same as [LenseFlareSetup] but all elements can be tweaked separately.

Note: Some effects can have unpredictable behaviour if the point light position matches the camera look target.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Brightness** (Single) | Controls the intensity of all elements listed in the advanced options. |
| **RandomSeed** (Int32) | Automatically creates different combinations of LenseFlares.<br/><br/>Elements include: MultiIris |
| **LightIndex** (Int32) | Select the Index / ID of the Pointlight to which the Lensflare is attached. |
| **RandomizeColor** (Vector4) | Select a color to tint various elements. |
| **Digital** (Single) | Intensity of secondary digital bars. |
| **Star** (Single) | Brightness and magnitude of the primary stellar element in the center of the light. |
| **Center** (Single) | Intensity and magnitude of the primary element in the center of the light. |
| **ColorEdgeGlow** (Single) | Intensity and size of the secondary blobs at the edge of the flare. |
| **MultiIris** (Single) | Intensity and transparency of the secondary lens reflections. |
| **Sparkle** (Single) | Visibility and intensity of the secondary rainbow glitter at the edge of the image. |
| **Shimmer** (Single) | Brightness and intensity of the primary glowing god rays. |
| **FlareSprites** (Single) | Brightness and intensity of the primary horizontal streak. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

