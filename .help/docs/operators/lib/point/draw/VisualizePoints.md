# VisualizePoints

*in [Lib.point.draw](README.md)*

This helper operator visualizes points and their orientation. It is permanently visible, but you can toggle the default settings for these gizmos using the "Gizmo" toggle in the output window.

Understanding the size, order, and orientation of points in space can be crucial for content creation.

Depending on the use case, this operator can help you visualize:

   The centers
   The "w" attribute (represented by the scale of the center points)
   The "w" attribute (listed as the connected table on the left)
   The orientation with its axes: x (right), y (up), z (forward)
   The orientation as sprites (indicated with an "F")
   The velocity of the points encoded in the rotation (relevant only for Simulation operators).

Also, please take a look at the presets.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Visibility** (GizmoVisibility) | — |
| **ShowCenterPoints** (Boolean) | — |
| **ShowAxis** (Boolean) | — |
| **ShowIndices** (Boolean) | — |
| **Size** (Single) | — |
| **LineThickness** (Single) | — |
| **Color** (Vector4) | — |
| **PointSize** (Single) | — |
| **UseWForSize** (Boolean) | — |
| **ShowAttributeList** (Boolean) | — |
| **StartIndex** (Int32) | — |
| **ShowVelocity** (Boolean) | — |
| **ShowSpritePlane** (Boolean) | — |
| **SpriteSize** (Single) | — |
| **ShowXyRadius** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

