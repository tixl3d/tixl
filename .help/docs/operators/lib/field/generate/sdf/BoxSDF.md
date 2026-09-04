# BoxSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural box field with rounded edges which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
Also known as Cube SDF.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [CylinderSDF], [ChainLinkSDF], [FractalSDF], [OctahedronSDF].

To use a Mesh made of polygons refer to [CubeMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Size** (Vector3) | Defines the size of the box<br/>x = width<br/>y = height<br/>z = depth |
| **UniformScale** (Single) | Scales the box uniformly in all directions |
| **EdgeRadius** (Single) | Defines how much the edge is rounded / beveled |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

