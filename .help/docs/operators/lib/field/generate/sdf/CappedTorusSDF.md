# CappedTorusSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural torus field which can be capped (with rounded edges).
It can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
Also known as Cube SDF.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [ChainLinkSDF], [FractalSDF].

To use a mesh made of polygons refer to [TorusMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Fill** (Single) | Defines which part of the torus is capped.<br/>0.930 = the torus is completely capped<br/>3.218 = the torus is uncapped |
| **Radius** (Single) | Defines the size of the ring |
| **Thickness** (Single) | Defines the thickness of the ring |
| **Axis** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

