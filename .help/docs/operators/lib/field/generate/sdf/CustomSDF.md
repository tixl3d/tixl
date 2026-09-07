# CustomSDF

*in [Lib.field.generate.sdf](README.md)*

Creates a custom SDF with optional parameters.
The default code generates a procedural sphere field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
For more Fractals check the different variations.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

The result can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [CapsuleLineSDF], [FractalSDF].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Offset** (Vector3) | — |
| **A** (Single) | — |
| **B** (Single) | — |
| **C** (Single) | — |
| **DistanceFunction** (String) | — |
| **AdditionalDefines** (String) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

