# VdbToSdfField

*in [Lib.volume.fields](README.md)*

Exposes a NanoVDB level set as a signed-distance field, so every existing field operator works on it.

Point Grid at the Buffer output of [LoadNanoVdb] or [VolumeToNanoVdb], and map BoundsMin/BoundsMax from the same operator's WorldMin/WorldMax. Outside that box the field returns the distance to the box plus the positive part of the value, so [RaymarchField] never steps into the volume blind.

With this, [RaymarchField] renders the surface, [FieldVolumeForce] and [FieldDistanceForce] make particles collide with it, [SampleFieldPoints] writes distances onto points, and [SDFToColor] shades by distance - all without new operators.

AKA: vdb sdf, nanovdb field, level set field

Tips
- Keep the grid's own transform in the grid: use TiXL's field transform operators to move or scale the result instead of rebuilding.
- The value is trilinearly sampled; a level set that is not a true narrow-band SDF will march unevenly.
- Bounds drive early rejection only; a wrong box shows up as the volume being clipped away.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **OutsideValue** (Single) | Clamp for the value far outside the box, so a distant march step stays finite. |
| **ValueOffset** (Single) | Added after scaling; use it to move the extracted isosurface. |
| **ValueScale** (Single) | Multiplies the sampled distance, for grids stored in different units. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |


