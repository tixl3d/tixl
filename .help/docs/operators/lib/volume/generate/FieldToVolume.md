# FieldToVolume

*in [Lib.volume.generate](README.md)*

Samples a CPU scalar field into a sparse grid over a box region.

Treat the field as a signed distance to get a valid level set, or as density for a fog volume. The box is sampled completely, so the cost grows with the cube of Size divided by VoxelSize and is capped at about 16.7 million samples.

AKA: field to SDF, bake field, rasterize field

Tips
- Combine with [VolumeToField] to resample or reshape a volume through any field operator.
- In Sdf mode the deep interior keeps its sign, so the solid does not read as outside.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Center** (Vector3) | World-space center of the sampled box. |
| **Mode** (Int32) | Sdf treats field values as signed distances and builds a level set. Fog treats them as density and keeps non-zero samples. |
| **Size** (Vector3) | Full extents of the sampled box in world units. All components must be positive. |
| **Field** (ScalarField) | The scalar field to sample, for example from [CustomScalarField] or [VolumeToField]. |
| **VoxelSize** (Single) | Edge length of one voxel in world units. The sample count is limited, so very small values produce no volume. |

## Outputs

| Name | Type |
|---|---|
| **Volume** | T3.Core.DataTypes.Volume.VdbVolume |


