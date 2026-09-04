# SoftTransformPoints

*in [Lib.point.transform](README.md)*

Transforms points inside a volume. Experimenting with different FallOff parameters can provide a wide variety of effects.

We provide the rotation not as three combined Euler angles to allow multiple revolutions.

This Operator is almost identical to combining [SelectPoints] -> [TransformPoints].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Amount** (Single) | — |
| **Translate** (Vector3) | — |
| **Dither** (Single) | — |
| **Stretch** (Vector3) | — |
| **Scale** (Single) | — |
| **Rotate** (Vector3) | — |
| **ScaleW** (Single) | — |
| **OffsetW** (Single) | — |
| **VolumeCenter** (Vector3) | — |
| **VolumeType** (Int32) | — |
| **VolumeStretch** (Vector3) | — |
| **VolumeSize** (Single) | — |
| **FallOff** (Single) | — |
| **Bias** (Single) | — |
| **UseWAsWeight** (Boolean) | — |
| **Visibility** (GizmoVisibility) | — |
| **GainAndBias** (Vector2) | — |
| **StrengthFactor** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

