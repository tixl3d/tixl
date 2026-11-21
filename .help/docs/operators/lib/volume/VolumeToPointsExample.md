# VolumeToPointsExample

*in [Lib.volume](README.md)*

One torus volume, three outputs side by side. Left: [VolumeToMesh] extracts triangles. Middle: [VolumeToPoints] turns the voxels near the surface into points, drawn together with the boxes of the volume's sparse tree from [DrawVdbBounds] - only where there is surface is there data. Right: [VolumeToTexture3D] bakes a dense 3D texture and [VolumeField] makes it a field that [RaymarchField] draws.

AKA: volume to points, point cloud, voxels, sparse tree, raymarch, 3d texture, convert

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- Meshes suit shading and physics, points suit particles, the texture suits raymarching and SDF combinations.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


