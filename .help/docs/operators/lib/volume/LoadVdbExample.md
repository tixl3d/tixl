# LoadVdbExample

*in [Lib.volume](README.md)*

[LoadVdb] reads OpenVDB files as written by Houdini, Blender or TiXL itself. The bunny is a level set: made coarser with [VolumeResample], meshed and scaled down. The plume is a fog volume: [VolumeToNanoVdb] uploads it and [VdbDensityField] makes its density a field for [RaymarchField]. [VolumeInfo] reports what a file contains.

AKA: vdb, openvdb, houdini, import, file, bunny, smoke, cloud

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- Files are watched and reload when they change on disk.
- A file with several grids: pick one with GridName.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


