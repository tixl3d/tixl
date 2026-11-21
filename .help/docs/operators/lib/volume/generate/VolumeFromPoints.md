# VolumeFromPoints

*in [Lib.volume.generate](README.md)*

Splats a point list into a sparse density volume.

Every point contributes a falloff kernel over its radius, and overlapping contributions add up. This is OpenVDB's particles-to-volume conversion and is the first step of a liquid surface: points become density, a filter smooths it, and [VolumeToMesh] extracts the surface.

AKA: particles to volume, splat, metaballs, point cloud to density

Tips
- Feed GPU points through [ReadPointsFromGpu] first.
- Pick the Radius around two to four voxel sizes. Smaller radii make separate beads, larger ones merge into a blob.
- Radius is limited to 64 voxels; increase VoxelSize for a wider kernel.
- Extract the surface with an isovalue between 0 and the peak density, and smooth the volume first with [VolumeFilter].

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **VoxelSize** (Single) | Edge length of one voxel in world units. |
| **Points** (StructuredList) | The points to splat. Only positions are used. |
| **Density** (Single) | Peak contribution of a single point at its center. Overlaps add up. |
| **Falloff** (Int32) | Shape of the kernel: Gaussian, Smoothstep or Constant. |
| **Radius** (Single) | Radius of the falloff kernel around every point in world units. |

## Outputs

| Name | Type |
|---|---|
| **Volume** | T3.Core.DataTypes.Volume.VdbVolume |


