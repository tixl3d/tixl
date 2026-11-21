# VolumeResample

*in [Lib.volume.modify](README.md)*

Changes a volume's voxel size by resampling its values trilinearly.

Smaller voxels give finer detail and more memory; larger voxels are cheaper. The sign of a solid's interior is preserved beyond the band, and fog density outside the source's active region stays empty.

AKA: rescale, change resolution, remesh grid

Tips
- Resample before expensive filters to speed up previews, and again afterwards for the final quality.
- A result that would need more than about 500 MB of voxel data is rejected with a log warning; use a larger VoxelSize.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Volume** (VdbVolume) | The volume to resample. |
| **VoxelSize** (Single) | New voxel edge length in world units. Zero or negative passes the volume through unchanged. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Volume.VdbVolume |


