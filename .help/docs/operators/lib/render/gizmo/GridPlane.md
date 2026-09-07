# GridPlane

*in [Lib.render.gizmo](README.md)*

A helper gizmo that can use a fragment shader to draw smooth grid lines.

This is what is visible in the viewport when "show gizmo and floor grid" is activated in an output window.

Also known as: Floor Grid, grid, coordinate system, graticule, 

If you want to apply Fog consider using [DrawLineGrid] which uses line geometry.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Color** (Vector4) | Defines the color of the grid |
| **Size** (Single) | Defines how far the grid extends into the distance |
| **Scale** (Single) | Defines the distance between the lines in the grid |
| **Rotation** (Vector3) | Rotates the grid around its own axis<br/><br/>Notice: The shader does not currently support all orientations and produces unpredictable results at some angles |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

