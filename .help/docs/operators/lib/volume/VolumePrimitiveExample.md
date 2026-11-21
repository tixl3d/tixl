# VolumePrimitiveExample

*in [Lib.volume](README.md)*

[VolumePrimitive] builds a shape directly as a level set: a sparse grid of signed distances that is exact near the surface. Eight primitives stand on the floor here, each meshed with [VolumeToMesh] and drawn lit.

A level set is not a mesh: it can be combined with [VolumeCsg], displaced with [VolumeNoise], grown with [VolumeMorphology] and only turned into triangles at the end.

AKA: vdb, openvdb, level set, sdf grid, voxel shape, primitive

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- Size is the full extent; a sphere reads only its X.
- BandWidth is how many voxels of exact distance surround the surface; widen it before heavy morphology.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


