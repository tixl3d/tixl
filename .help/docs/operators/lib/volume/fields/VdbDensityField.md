# VdbDensityField

*in [Lib.volume.fields](README.md)*

Exposes a NanoVDB fog volume as a field with the usual negative-inside convention.

The output is IsoValue minus the sampled density, so dense smoke reads as "inside" to operators that expect a signed distance. Point Grid at the Buffer output of [LoadNanoVdb] or [VolumeToNanoVdb], and map BoundsMin/BoundsMax from the same operator's world bounds.

Use it where a level set makes no sense but the density still has to drive something: [FieldVolumeForce] to drag particles into smoke, [SampleFieldPoints] to write density onto points, or [SDFToColor] for shaded cross-sections.

AKA: vdb density, smoke field, fog field

Tips
- Set IsoValue to the density that should count as the surface; 0.5 suits normalized smoke.
- Raise ValueScale when the grid stores density in a small range.
- The result is not a true distance, so avoid marching it with fine steps in RaymarchField; use NanoVdbRender for volume shading.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **OutsideValue** (Single) | Value reported outside the grid box. |
| **IsoValue** (Single) | Density treated as the surface; higher values shrink the inside region. |
| **ValueOffset** (Single) | Added after scaling, to shift the density range. |
| **ValueScale** (Single) | Multiplies the sampled density before the iso comparison. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |


