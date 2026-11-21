# PhysicsVolumesExample

*in [Lib.physics](README.md)*

Any volume becomes a collider through [VolumeToMesh]. On the left, the bunny is loaded with [LoadVdb], made coarser with [VolumeResample] and scaled down; on the right, a bowl is carved out of a block with [VolumeCsg]. Balls rain on both.

AKA: vdb, nanovdb, openvdb, level set, voxel collider, csg, bunny

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- A coarser VoxelSize gives fewer triangles and a cheaper collider.
- A volume that changes rebuilds the collider, so animate it sparingly.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


