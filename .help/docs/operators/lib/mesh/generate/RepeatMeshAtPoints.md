# RepeatMeshAtPoints

*in [Lib.mesh.generate](README.md)*

Creates a new mesh that repeats the incoming mesh at each point.
Note: 
Warning: With detailed meshes, or very large scaled meshes and/or a very high number of points, this Operator can take up a lot of resources.

Also see: [DrawMeshAtPoints]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | Input for Points such as [SpherePoints], [GridPoints] or [PointsOnMesh] etc. |
| **InputMesh** (MeshBuffers Required) | Input for Meshes like [LoadObj], [TorusMesh] etc. |
| **Scale** (Single) | Defines the size of the meshes that are distributed at the points. |
| **ScaleFactor** (Int32) | — |
| **ApplyPointScale** (Boolean) | Controls whether the "Stretching" values from operators such as [RandomizePoints] or [TransformPoints] are used. |
| **Stretch** (Vector3) | Non-Uniformly scales the Meshes. |
| **TexCoord2Factor** (Int32) | Shift TexCoord2 vertically, useful to unsync VAT animations |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshBuffers |

