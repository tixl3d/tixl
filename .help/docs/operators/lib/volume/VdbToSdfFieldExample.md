# VdbToSdfFieldExample

*in [Lib.volume](README.md)*

[VdbToSdfField] makes a volume's signed distances a field, so it mixes with every SDF operator. Here a torus volume is blended smoothly with an animated [SphereSDF] by [CombineSDF], placed on an SDF floor and raymarched. Nothing is meshed.

AKA: sdf, signed distance field, raymarch, blend, metaball, smooth union

Tips
- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.
- Level sets store signed distance in world units (negative inside); fog volumes store density.
- K is the blend radius of the smooth union; 0 gives a hard edge.
- Any volume works, also one loaded with [LoadVdb].

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


