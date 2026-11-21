# VolumeFromMeshExample

*in [Lib.volume](README.md)*

Left: the word TiXL as geometry becomes a level set with [VolumeFromMesh], and [VolumeMorphology] dilates it into soft bubble letters. Middle: scattered points are splatted into a density volume with [VolumeFromPoints], a metaball cloud. Right: a noise field is sampled over a box with [FieldToVolume].

Whatever goes in, what comes out is a volume, and every volume operator works on it.

AKA: voxelize, mesh to sdf, points to volume, metaballs, splat, field to volume

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- A density volume is meshed at a density Isovalue, not at 0.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


