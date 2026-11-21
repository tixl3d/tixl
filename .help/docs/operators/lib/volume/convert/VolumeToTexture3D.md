# VolumeToTexture3D

*in [Lib.volume.convert](README.md)*

Bakes a volume into a dense 3D texture plus its world-space bounds.

This is the GPU bridge for raymarching: the texture holds the distance or density values as R32 float, and the bounds map them back into world space.

AKA: voxel texture, 3D texture, bake volume

Tips
- Connect the texture and both bounds to [VolumeField] and feed that into [RaymarchField].
- The texture grows with the cube of the resolution. Keep MaxResolution modest.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Volume** (VdbVolume) | The volume to bake. Only the first float grid is used. |
| **MaxResolution** (Int32) | Largest number of texels along any axis (8 to 2048). Larger volumes are clamped to this size. The total is additionally capped at 64M texels (256 MB), so extreme settings shrink evenly instead of exhausting memory. |

## Outputs

| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture3dWithViews |
| **BoundsMin** | System.Numerics.Vector3 |
| **BoundsMax** | System.Numerics.Vector3 |


