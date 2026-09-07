# GetScreenPos

*in [Lib.render.analyze](README.md)*

Returns the given position in screen space coordinates.

This can be useful to attach labels or annotations to objects rendered by a camera.

Please check out the example.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **LocalPosition** (Vector3) | Optional offset to the converted position |
| **SetDepthToZero** (Boolean) | Sets the z-component of the view space coordinate to z. <br/>In most cases, this is what you want for using directly as screen space position. |

## Outputs
| Name | Type |
|---|---|
| **UpdateCommand** | T3.Core.DataTypes.Command |
| **Position** | System.Numerics.Vector3 |

