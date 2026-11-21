# VolumeMorphology

*in [Lib.volume.modify](README.md)*

Morphological dilate and erode on volumes.

On level sets the surface moves outward (Dilate) or inward (Erode) by the radius; Open and Close apply both to remove thin features or fill pinholes. Fog volumes use min and max stencil filtering instead.

AKA: grow, shrink, offset surface, open, close

Tips
- Radius is in world units, so a radius of 0.1 on a sphere of radius 0.6 gives 0.7 or 0.5.
- Keep a few voxels of band around the surface, or large radii fall outside it.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Operation** (Int32) | Dilate grows, Erode shrinks, Open erodes then dilates, Close dilates then erodes. |
| **Radius** (Single) | Offset distance in world units. Zero or negative leaves the volume unchanged. |
| **Volume** (VdbVolume) | The volume to modify. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Volume.VdbVolume |


