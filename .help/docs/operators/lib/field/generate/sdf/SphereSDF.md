# SphereSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural sphere field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].

For a simple and interactive tutorial on the TiXL rendering pipeline for signed distance fields, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [ChainLinkSDF], [FractalSDF].

To use a Mesh made of polygons refer to [SphereMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | Transforms the center of the object |
| **Radius** (Single) | Defines the size / radius of the sphere field |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

