# TorusMesh

*in [Lib.mesh.generate](README.md)*

Generates a procedural three-dimensional torus mesh which can be rendered with [DrawMesh], [DrawMeshUnlit] and [DrawMeshHatched] among others.
Also known as: Donut

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].
To import static 3D meshes from other programs refer to [LoadObj].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Radius** (Single) | Ring size. |
| **Thickness** (Single) | Ring thickness. |
| **Segments** (Int2) | Controls the tessellation.<br/>1. Along the Radius.<br/>2. Along the Thickness. |
| **Spin** (Vector2) | Slides the geometry.<br/>1. Along the Radius.<br/>2. Around the Thickness. |
| **Fill** (Vector2) | Cuts and compresses / slides the geometry.<br/>X. Along the Radius.<br/>Y. Along the Thickness. |
| **SmoothAngle** (Single) | Defines at what angle two adjacent faces share a smoothing group. |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

