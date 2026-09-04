# SimPointMeshCollisions

*in [Lib.point.sim.experimental](README.md)*

Simulates collisions with meshes.

Note: this is VERY expensive with large meshes (i.e., with more than 100 faces).


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **PointsA_** (BufferWithViews) | — |
| **Bouncyness** (Single) | — |
| **ClampAccelleration** (Single) | — |
| **Damping** (Single) | — |
| **IsEnabled** (Boolean) | — |
| **Mesh** (MeshBuffers Required) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

