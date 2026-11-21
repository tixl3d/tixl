# VolumeToField

*in [Lib.volume.convert](README.md)*

Wraps a volume in a CPU scalar field that samples it trilinearly in world space.

The field plugs into everything that consumes scalar fields, such as [DisplaceGeometry], [ScatterPointsInVolume] density or field SDF combinations.

AKA: volume as field, sample volume

Tips
- Outside the active bounds the field returns OutsideValue, so use a large positive value for signed distance fields.

The Vectors output is the volume's vector grid, such as a cached velocity, or the gradient of its float grid when there is none. Connect it to the Field input of [RigidForce] to steer rigid bodies with a volume. Scale multiplies it; outside the volume it is zero.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Scale** (Single) | Multiplies the sampled value. |
| **Offset** (Single) | Added to the sampled value after scaling. |
| **OutsideValue** (Single) | Value returned for positions outside the bounding box of the active voxels. |
| **Volume** (VdbVolume) | The volume to sample. Only the first float grid is used. |

## Outputs

| Name | Type |
|---|---|
| **Field** | T3.Core.DataTypes.ScalarField |
| **Vectors** | T3.Core.DataTypes.VectorField |


