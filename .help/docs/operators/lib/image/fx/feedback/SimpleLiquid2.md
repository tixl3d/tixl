# SimpleLiquid2

*in [Lib.image.fx.feedback](README.md)*

This Simple Detailed Fluid effect is a port of 'https://www.shadertoy.com/view/sl3Szs' by Lomateron.

Parameters:

It yields very interesting results when combined with an FX Texture that can include the following channels:
- RB -> Velocity vectors (0.5 is neutral)
- G -> Mass
- A -> Effect Amount

Each of these channels can be multiplied with the ApplyFxTexture parameter.

The SpeedFactor is applied on each frame and can be used for adding friction (values smaller than 1) or dynamism (values larger than 1) to the system.
The StabilizeFactor will lower too intense and amplify too low mass values.
The iteration count will speed up the processing of the application.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TriggerReset** (Boolean) | — |
| **Gravity** (Vector2) | — |
| **BorderStrength** (Single) | — |
| **MassAttraction** (Single) | — |
| **ApplyFxTexture** (Single) | — |
| **FX_RG_Velocity** (Single) | — |
| **SpeedFactor** (Single) | — |
| **FX_B_AddRemoveMass** (Single) | — |
| **StabilizeFactor** (Single) | — |
| **ResetFillFactor** (Single) | — |
| **MouseClick_Force** (Single) | — |
| **OnClick_AddRemoveMass** (Single) | — |
| **FxTexture** (Texture2D) | — |
| **Iterations** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **ColorBuffer** | T3.Core.DataTypes.Texture2D |

