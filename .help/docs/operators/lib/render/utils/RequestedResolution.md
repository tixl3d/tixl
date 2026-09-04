# RequestedResolution

*in [Lib.render.utils](README.md)*

Has the function of extracting the resolution from an operator that is initialized later (for example a [Rendertarget]) in order to use it at an earlier point in the node tree.

To do this, you can connect the 'Resolution' input of a render target to the output of the 'RequestResolution'. The requested resolution can now be extracted and used again.

Also see [SetRequestedResolution]

## Outputs
| Name | Type |
|---|---|
| **Size** | T3.Core.DataTypes.Vector.Int2 |
| **Width** | System.Int32 |
| **Height** | System.Int32 |
| **AspectRatio** | System.Single |

