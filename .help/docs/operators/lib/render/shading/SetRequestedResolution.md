# SetRequestedResolution

*in [Lib.render.shading](README.md)*

Set the requested resolution (similar to the Resolution drop-down of the output windows).

Please be sure to understand how Tooll handles resolutions before using this operator. Check the tutorial linked below.

Also see: [RequestedResolution]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D) | — |
| **Resolution** (Int2) | Enforces a new resolution if both of these are greater than 1. |
| **ScaleResolution** (Single) | An optional scaling factor that is applied to either the original or the new requested resolution. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Texture2D |

