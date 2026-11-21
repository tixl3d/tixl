# VolumeToPoints

*in [Lib.volume.convert](README.md)*

Converts volume voxels into a point list.

Either the active voxels above a threshold (highest values first), or a uniform random subset of them. The same points are also uploaded to a GPU buffer for point renderers.

AKA: voxels to points, scatter, sample volume

Tips
- Use Scatter mode for a stable, evenly spread cloud and ActiveVoxels mode to keep the densest voxels.
- Level sets have negative values inside; lower Isovalue to include them.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Mode** (Int32) | ActiveVoxels keeps the highest-valued voxels when there are more than MaxPoints. Scatter keeps a uniform random subset. |
| **Isovalue** (Single) | Only voxels with a value above this threshold become points. |
| **Seed** (Int32) | Random seed for Scatter mode. The same seed gives the same points. |
| **Volume** (VdbVolume) | The volume to read. Only active voxels are considered. |
| **MaxPoints** (Int32) | Upper limit of the point count (up to 2,000,000). |

## Outputs

| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.StructuredList |
| **GPoints** | T3.Core.DataTypes.BufferWithViews |


