# TextureToCubeMap

*in [Lib.render.shading](README.md)*

Converts a 2d texture ([loaded with [LoadImage]) into a cube map that can then be used by [SetEnvironment] for PBR image-based lighting.
Currently Tooll supports Equirectangular Cubemaps.

If changing values and settings in this Operator does not cause any changes in the Viewport, it may be necessary to (temporarily) activate 'UpdateLive' in the following [SetEnvironment] op.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **Orientation** (Single) | Rotates the texture along the horizon.<br/><br/>If changing this value does not cause any changes in the Viewport, it may be necessary to activate 'UpdateLive' in the following [SetEnvironment] op. |
| **Resolution** (Int32) | Defines the resolution in pixels |

## Outputs
| Name | Type |
|---|---|
| **OutputTexture** | T3.Core.DataTypes.Texture2D |

