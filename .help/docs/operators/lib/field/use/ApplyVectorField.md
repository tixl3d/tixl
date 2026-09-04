# ApplyVectorField

*in [Lib.field.use](README.md)*

Applies a signed XYZ vector field by setting the rotation and F1 to a set of points.

Ideally this requires a _vector_ field, not a color or SDF, with signed vectors for the 3 axis in world space.

Also see [SdfToVector]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **Strength** (Single) | — |
| **StrengthFactor** (Int32) | — |
| **Normalize** (Boolean) | — |
| **VectorField** (ShaderGraphNode Required) | — |
| **UpVector** (Vector3) | — |
| **ClampLength** (Single) | — |
| **ScaleLength** (Single) | — |
| **SetFx1To** (Int32) | — |
| **SetFx2To** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Result2** | T3.Core.DataTypes.BufferWithViews |

