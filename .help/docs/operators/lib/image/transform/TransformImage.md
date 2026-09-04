# TransformImage

*in [Lib.image.transform](README.md)*

Rotates, offsets, and scales the incoming image.
It includes texture wrapping modes: Repeat, Mirror, Clamp, Border...
It's a powerful and versatile operator with many use cases such as tiling textures, patterns, decals...

To control how the image changes when it's repeating use: [MirrorRepeat]
A more sophisticated version: [KochKaleidoskope]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Offset** (Vector2) | Moves the incoming image |
| **Stretch** (Vector2) | Scales the incoming image in the following directions:<br/><br/>X: Width<br/>Y: Height |
| **Scale** (Single) | Linearly scales the incoming image |
| **Rotation** (Single) | Rotates the incoming image |
| **Resolution** (Int2) | Overwrites the resolution of the incoming image |
| **ResolutionFactor** (Vector2) | This can be useful to scale down and  |
| **GenerateMips** (Boolean) | Defines whether mipmaps should be generated |
| **Filter** (Filter) | Defines the image filtering used |
| **WrapMode** (Int32) | Defines how the area around the image will be rendered |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

