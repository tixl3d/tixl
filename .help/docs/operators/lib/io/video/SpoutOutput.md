# SpoutOutput

*in [Lib.io.video](README.md)*

Spout live video output. We recommend using the R8G8B8A8_UNorm texture format for output. You can adjust the render format by using a [RenderTarget] like in this example: [TorusMesh]->[DrawMesh]->[RenderTarget]->[SpoutOutput].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D Required) | — |
| **SenderName** (String) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

