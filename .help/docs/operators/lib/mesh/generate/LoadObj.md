# LoadObj

*in [Lib.mesh.generate](README.md)*

Loads a mesh from an obj. file by generating Vertex and Index buffers which can be rendered with [DrawMesh], [DrawMeshUnlit] and [DrawMeshHatched] among others.
All surfaces must have valid UVs, otherwise they will appear black.

For a more comfortable import of 3d meshes (which also allows the import of already linked materials) use [LoadGltfScene] in combination with [DrawScene].

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].

To generate meshes procedurally with many adjustment options see [CubeMesh], [CylinderMesh], [QuadMesh], [SphereMesh] or [TorusMesh] etc.



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Path** (String) | Use "..." to select a Wavefront .obj file to import |
| **UseGPUCaching** (Boolean) | Toggle GPU Caching |
| **ClearGPUCache** (Boolean) | Enable and disable to clear GPU cache |
| **SortVertices** (Int32) | Check to sort vertices |
| **ScaleFactor** (Single) | Uniformly scales the loaded mesh |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |

