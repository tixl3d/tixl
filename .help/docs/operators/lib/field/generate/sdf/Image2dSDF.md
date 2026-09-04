# Image2dSDF

*in [Lib.field.generate.sdf](README.md)*

Uses the grayscale information of a texture as (signed) distance data.
This works nicely with [JumpFloodFill]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SdfImage** (Texture2D Required) | — |
| **SdfScale** (Single) | — |
| **ImageSize** (Vector2) | — |
| **Offset** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

