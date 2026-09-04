# NdiOutput

*in [Lib.io.video](README.md)*

NDI live video output. R8G8B8A8_UNorm texture format or R8G8B8A8_Typeless is required for output. You can adjust the render format by using a ConvertFormat op or [RenderTarget] like in this example: [TorusMesh]->[DrawMesh]->[RenderTarget]->[SpoutOutput].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D) | — |
| **SenderName** (String) | — |
| **FrameRate** (Int32) | — |
| **EnableAlpha** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

