# VolumeShakingWaterExample

*in [Lib.volume](README.md)*

The container is an open box: a box minus a slightly smaller box with [VolumeCsg] Difference. The water is a slab displaced by [VolumeNoise] and clipped by Intersection to the inside of the box. Two [OscillateVec3] make the box shake and the water slosh.

AKA: water, liquid, slosh, container, csg, noise, animation

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


