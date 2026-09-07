# PickColorFromImage

*in [Lib.numbers.color](README.md)*

Gets the color of a certain position in the texture

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputImage** (Texture2D Required) | — |
| **Position** (Vector2) | Defines the point of the image whose color is output on the X and Y axis.<br/>X: 0 Left / 1 Right<br/>Y: 0 Top / 1 Bottom |
| **AlwaysUpdate** (Boolean) | If activated, the incoming image is constantly updated. Helpful if the image is not static but changes over time (video / animation etc.) |

## Outputs
| Name | Type |
|---|---|
| **Output** | System.Numerics.Vector4 |

