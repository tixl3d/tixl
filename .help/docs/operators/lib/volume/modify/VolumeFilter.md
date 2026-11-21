# VolumeFilter

*in [Lib.volume.modify](README.md)*

Smoothing and value filters on the active voxels.

Gaussian and Box smooth noisy data, Median removes outliers, and Offset shifts all values. Inactive voxels keep their values, so the sign of a solid's interior is preserved.

AKA: blur, smooth, denoise, median

Tips
- Smooth a point density volume before extracting a liquid surface.
- Radius is rounded to whole voxels, so values below half a voxel have no effect.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Volume** (VdbVolume) | The volume to filter. |
| **Iterations** (Int32) | How many times the filter is applied (1 to 10). |
| **Radius** (Single) | Filter radius in world units. It is converted to whole voxels (up to 8; the median uses 1 or 2). |
| **Offset** (Single) | Value added to every voxel. Used by the Offset filter only. |
| **FilterType** (Int32) | Gaussian, Box, Median or Offset. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Volume.VdbVolume |


