# VolumeCsg

*in [Lib.volume.modify](README.md)*

Constructive solid geometry on level-set volumes.

Union merges, Intersection keeps the overlap, and Difference carves B out of A. The result is a fresh level set. Volumes with different voxel sizes or centers are combined in the finer grid.

AKA: boolean, union, intersection, difference, subtract

Tips
- Chain several operators to build complex solids from primitives.
- Both inputs must be signed distance fields.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **VolumeB** (VdbVolume) | The second level set. For Difference this is the tool that is removed. |
| **VolumeA** (VdbVolume) | The first level set. For Difference this is the solid that is carved. |
| **Operation** (Int32) | Union, Intersection or Difference (A minus B). |

## Outputs

| Name | Type |
|---|---|
| **Volume** | T3.Core.DataTypes.Volume.VdbVolume |


