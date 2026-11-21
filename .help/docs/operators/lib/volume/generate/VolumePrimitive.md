# VolumePrimitive

*in [Lib.volume.generate](README.md)*

Generates an analytic primitive as a narrow-band level set volume.

The result is an exact signed distance field: negative inside, positive outside, with values stored in world units. Voxels deeper than the band keep only their sign. Use it as the starting point for [VolumeCsg], [VolumeNoise], [VolumeMorphology] and [VolumeToMesh].

AKA: SDF, sphere, box, torus, level set primitive

Tips
- Size is the full extent, not the radius. A sphere uses the X component only.
- Center moves the whole grid, so two primitives with different centers live in different index spaces. CSG and math resample them for you.
- Smaller VoxelSize means sharper surfaces and quadratically more memory.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Size** (Vector3) | Full extents of the shape in world units. A sphere reads only X as its diameter. |
| **VoxelSize** (Single) | Edge length of one voxel in world units. Smaller values give a sharper surface and cost memory and time. |
| **BandWidth** (Single) | Half-width of the narrow band in voxels (1 to 64). Operators such as filters and morphology need a few voxels of band to work with. |
| **Center** (Vector3) | World-space position of the shape. The voxel grid is translated with it, so the surface stays aligned to voxel centers. |
| **Shape** (Int32) | Which primitive to build: Sphere, Box, RoundedBox, Torus, Capsule, Cylinder, Cone, Octahedron or Plane. |

## Outputs

| Name | Type |
|---|---|
| **Volume** | T3.Core.DataTypes.Volume.VdbVolume |


