# DistortAndShade

*in [Lib.image.fx.distort](README.md)*

Uses two images to distort (create a bevel / emboss effect) that can look like textured glass

Also see [Displace]

For similar effects see: [Steps] [DetectEdges] [FakeLight]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D Required) | — |
| **ImageB** (Texture2D Required) | — |
| **Displacement** (Single) | Controls the direction and intensity of the effect |
| **Center** (Vector2) | Shifts the direction from which image A is distorted by image B |
| **Shading** (Single) | Controls the visibility / brightness of image B |
| **ShadeColor** (Vector4) | Defines a color which is multiplied with Image B<br/><br/>(Only visible when the 'shading' value is increased) |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

