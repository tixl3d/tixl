# RenderWithMotionBlur

*in [Lib.image.use](README.md)*

This will render multiple instances of the incoming op each pass with slightly offset local time. 
Its primary purpose is to increase rendering quality for output with Render to Image Sequences or Video. Depending on the complexity of the graph, this effect can be very slow. You can use [HasTimeChanged] with Mode=AdvancedWithMotionBlur to trigger updates to the first pass. This can be useful for simulation or feedback effects. Tip: When rendering videos or sequences, you can override the sample count.

As an alternative: See [MotionBlur] for an operator that requires far fewer resources because it uses real-time camera data such as motion vectors.

Also see: [DepthOfField]


Useful Ops for a PostFX Pipeline: [MotionBlur] [DepthOfField] [ChromaticAbberation] [Glow] [Grain] [Blur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D Required) | — |
| **Passes** (Int32) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |
| **Strength** (Single) | Controls the strength of the blur.<br/><br/>ProTip: For precise adjustment, it can be helpful to reduce the passes to "2" making the strength clearly visible. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

