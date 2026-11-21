# VolumeNoiseExample

*in [Lib.volume](README.md)*

Three stages of one chain, left to right. [VolumeNoise] in Displace mode pushes the sphere's surface along fractal noise. [VolumeMorphology] with Dilate grows it outwards, which swells the bumps and closes the dents, and [VolumeFilter] smooths the voxels, which softens what is left.

AKA: displace, noise, blob, organic, smooth, blur, erode, dilate, open, close

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- Animated volumes are rebuilt on the CPU every frame; keep their voxels coarse.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


