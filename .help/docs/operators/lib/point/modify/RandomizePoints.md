# RandomizePoints

*in [Lib.point.modify](README.md)*

Smoothly randomizes various point attributes. It's an extremely versatile operator that provides various options of applying the random modifications and can be smoothly animated.

Note:  This is an updated version of [_RandomizePoints_Legacy1]. It supports phase seeds, colors, extends and different randomization methods and an improved BiasGain method.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Strength** (Single) | — |
| **StrengthFactor** (Int32) | — |
| **Position** (Vector3) | — |
| **Rotation** (Vector3) | — |
| **F1** (Single) | — |
| **F2** (Single) | — |
| **ColorHSB** (Vector4) | — |
| **Scale** (Single) | — |
| **Stretch** (Vector3) | — |
| **RandomPhase** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **OffsetMode** (Int32) | — |
| **Space** (Int32) | — |
| **Interpolation** (Int32) | — |
| **Repeat** (Int32) | — |
| **ClampColorsEtc** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

