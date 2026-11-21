# VolumeToMesh

*in [Lib.volume.convert](README.md)*

Extracts an isosurface from a volume as a triangle mesh.

Uses marching cubes with shared vertices and gradient normals. Isovalue 0 extracts the surface of a level set; for fog volumes pick a density cutoff. The mesh faces outward with counter-clockwise winding.

AKA: marching cubes, isosurface, surface extraction, meshing

Tips
- Render with [DrawMesh] after [GeometryToMesh].
- Smooth noisy results with [VolumeFilter] before extracting.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Volume** (VdbVolume) | The volume to mesh. Only the first float grid is used. |
| **Isovalue** (Single) | The value at which the surface is extracted. Zero for level sets; a density threshold for fog volumes. |

## Outputs

| Name | Type |
|---|---|
| **Geometry** | T3.Core.DataTypes.MeshGeometry |


