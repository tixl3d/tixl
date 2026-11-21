# NanoVdbToPoints

*in [Lib.volume.convert](README.md)*

Turns the active voxels of a NanoVDB grid into TiXL points on the GPU.

Point Grid at the Buffer output of [LoadNanoVdb] or [VolumeToNanoVdb], and connect a [ComputeShader] whose Path is Lib:shaders/volume/nanovdb-to-points.hlsl. The output is a point buffer for [DrawPoints], [PointsToCurve] or any other point operator.

Nothing is read back to the CPU: one thread group walks one leaf node and appends the voxels above Threshold. The unused tail of the buffer is filled with separators, so downstream operators stop at the real count. Voxel values land in FX1, which is handy for sizing or colouring by density.

AKA: vdb points, voxels to points, nanovdb particles

Tips
- MaxLeaves bounds the dispatch, one group per leaf; leaves past the grid's own count are skipped, so a value larger than needed only costs empty groups.
- Lower Threshold or raise MaxPoints when the result looks clipped.
- The shader assumes a float grid with the standard NanoVDB layout; vector grids need their own leaf stride.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Threshold** (Single) | Voxels below this value are skipped. Use the background value for fog volumes. |
| **MaxLeaves** (Int32) | Number of leaf nodes to dispatch. Larger than the grid is harmless; smaller clips the volume. |
| **MaxPoints** (Int32) | Capacity of the output buffer. Voxels past this many are dropped. |

## Outputs

| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.BufferWithViews |


