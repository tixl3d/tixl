# TorusSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural torus field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
Also known as: Donut SDF

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat], and more.

Similar nodes: [BoxSDF], [ChainLinkSDF], [FractalSDF].

To use a Mesh made of polygons refer to [TorusMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Radius** (Single) | Defines the main radius of the Torus |
| **Thickness** (Single) | Defines the thickness of the Torus |
| **Axis** (Int32) | Defines the axis along which the field is aligned |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

