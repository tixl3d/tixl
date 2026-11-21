# VolumeFromMesh

*in [Lib.volume.generate](README.md)*

Converts a mesh into a sparse volume, either a signed distance field or a fog density.

Distances are exact within a narrow band around the surface; the interior of a closed mesh is filled with the sign so the solid behaves correctly in CSG and offsets. The mesh needs consistent outward winding for the sign to be right.

AKA: mesh to SDF, voxelize, mesh to level set, mesh to fog

Tips
- Convert back with [VolumeToMesh] for remeshing, smoothing or boolean operations on arbitrary meshes.
- Open meshes have no inside. Close holes before converting, or use Fog mode.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **VoxelSize** (Single) | Edge length of one voxel in world units. This sets the resolution of the result. |
| **Mode** (Int32) | Sdf stores signed distances (negative inside). Fog stores a density from 0 to 1 that falls off from the surface across the band. |
| **BandWidth** (Single) | Half-width of the narrow band in voxels around the surface (1 to 64). Distances beyond the band are not stored. |
| **Geometry** (MeshGeometry) | The mesh to convert. It must contain faces. |

## Outputs

| Name | Type |
|---|---|
| **Volume** | T3.Core.DataTypes.Volume.VdbVolume |


