# SelectPoints

*in [Lib.point.modify](README.md)*

Simulates a selection of points by setting the F1 or F2 attribute.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Strength** (Single) | — |
| **StrengthFactor** (Int32) | — |
| **WriteTo** (Int32) | — |
| **Mode** (Int32) | — |
| **ClampResult** (Boolean) | — |
| **VolumeShape** (Int32) | — |
| **VolumeCenter** (Vector3) | — |
| **VolumeStretch** (Vector3) | — |
| **VolumeScale** (Single) | — |
| **VolumeRotate** (Vector3) | — |
| **FallOff** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Scatter** (Single) | — |
| **Phase** (Single) | — |
| **Threshold** (Single) | — |
| **Visibility** (GizmoVisibility) | — |
| **DiscardNonSelected** (Boolean) | Replace points with a selection of 0.0 with line separators. This can be relevant for instantiation on drawing connection lines between points.<br/>This does NOT change the size of the buffer. |
| **SetW** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Result2** | T3.Core.DataTypes.BufferWithViews |

