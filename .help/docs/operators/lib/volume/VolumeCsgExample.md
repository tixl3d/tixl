# VolumeCsgExample

*in [Lib.volume](README.md)*

[VolumeCsg] combines level sets like a sculptor: Union merges, Intersection keeps the overlap, Difference carves. Read the row from left to right: a rounded box intersected with a sphere gives the classic CSG body, three bars joined with Union make the drill, and the body minus the drill is the result on the right.

Because every step is again a level set, the steps chain without ever building a mesh in between.

AKA: boolean, union, intersection, difference, subtract, carve, csg

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- Volumes with different voxel sizes are combined into the finer grid.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


