# FractalSDF

*in [Lib.field.generate.sdf](README.md)*

Generates a procedural MandelBox field which can be rendered with [RaymarchField] and visualized with [VisualizeFieldDistance].
For more Fractals check [CustomSDF] with it's different Variations.

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToRaymarchField].

It can be modified with [BendField], [TransformField], [PolarRepeat] and more.

Similar nodes: [BoxSDF], [CapsuleLineSDF], [CustomSDF].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Scale** (Single) | The overall detail of the fractal. |
| **Minrad** (Single) | The complexity of the fractal. |
| **Clamping** (Vector3) | An internal parameter that shapes the fractal.<br/>The x and y components only have an effect with a z-component > 0. |
| **Increment** (Vector3) | An additional parameter to manipulate the fractal. |
| **Fold** (Vector2) | — |
| **Iterations** (Int32) | The quality and complexity of the fractal greatly depend on the number of iterations. So does the performance.<br/><br/>Iterations above 10 might not be able to raymarch in 1080p. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

