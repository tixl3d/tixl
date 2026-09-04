# CylinderSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural cylinder field with rounded edges which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [ChainLinkSDF], [FractalSDF].

To use a Mesh made of polygons refer to [CylinderMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Radius** (Single) | Defines the thickness / radius of the cylinder |
| **Height** (Single) | Defines the height of the cylinder |
| **Rounding** (Single) | Defines how much the edge of the cylinder is rounded / beveled |
| **Axis** (Int32) | Defines the axis along which the field is aligned |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

