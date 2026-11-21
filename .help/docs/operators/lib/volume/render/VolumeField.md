# VolumeField

*in [Lib.volume.render](README.md)*

Samples a baked 3D texture as a distance field for [RaymarchField].

This is how volumes get rendered with TiXL's field pipeline: bake the volume with [VolumeToTexture3D], then map the texture and its bounds one to one into this operator.

AKA: raymarch volume, 3D texture field, volume SDF

Tips
- Outside the bounds the field returns the distance to the bounding box plus the clamped edge value, so rays approach the volume without overshooting. OutsideValue caps that distance.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **BoundsMax** (Vector3) | World-space corner of the texture with the highest coordinates. Connect from [VolumeToTexture3D]. |
| **ValueOffset** (Single) | Added to the sampled value. Negative values grow the surface, positive values shrink it. |
| **ValueScale** (Single) | Multiplies the sampled value. Use values below 1 if the stored distances overestimate. |
| **BoundsMin** (Vector3) | World-space corner of the texture with the lowest coordinates. Connect from [VolumeToTexture3D]. |
| **OutsideValue** (Single) | Upper limit for the distance reported outside the bounds. Keep it large unless you want to clip the field. |
| **Texture** (Texture3dWithViews) | The baked volume texture from [VolumeToTexture3D]. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |


