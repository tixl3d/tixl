# MotionBlur

*in [Lib.render.postfx](README.md)*

Applies a post processing motion blur to a RenderTarget texture and its DepthBuffer.

See also [RenderWithMotionBlur] for an operator using multi-pass rendering to achieve high quality at the cost of more resources. 
Designed primarily for video rendering, it can also be used for real-time applications with just a few passes, in simple scenes or on fast hardware.

Also see [DepthOfField], [DirectionalBlur]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | — |
| **DepthMap** (Texture2D Required) | — |
| **CameraReference** (Object Required) | — |
| **SampleCount** (Int32) | Controls the fidelity of the effect.<br/>Higher numbers create smoother results at the cost of rendering speed. |
| **Strength** (Single) | Controls the strength of the blur.<br/><br/>ProTip: For precise adjustment, it can be helpful to reduce the passes to very low settings making the strength clearly visible. |
| **ClampEffect** (Single) | Controls how much the intensity of the motion blur of very fast image elements differs from slower ones.  |
| **VelocityOffset** (Vector3) | An additional offset that can be used to fake motion blur for static cameras. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

