# DepthBufferAsGrayScale

*in [Lib.image.use](README.md)*

Converts the provided depth buffer into a grayscale texture.

Important: Requires the correct settings for near/far clipping.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **NearFarRange** (Vector2) | — |
| **OutputRange** (Vector2) | — |
| **ClampOutput** (Boolean) | — |
| **Mode** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

