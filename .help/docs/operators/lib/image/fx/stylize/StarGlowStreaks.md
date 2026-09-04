# StarGlowStreaks

*in [Lib.image.fx.stylize](README.md)*

Renders streaks onto the incoming image. You can use the threshold to limit the range that is used for streaks. Also, check out the presets.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Color** (Vector4) | — |
| **Range** (Single) | — |
| **Brightness** (Single) | — |
| **Threshold** (Single) | — |
| **BlendMode** (Int32) | — |
| **OriginalColor** (Vector4) | — |
| **Quality** (Int32) | Experimental! 0 = Best quality, 7 = Lowest quality <br/>You need to activate GenerateMips on the RenderTarget if you want to use the lower qualities.<br/>As it's based on MipMaps, a lower quality should be less expensive. <br/> |
| **GlareModes** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

