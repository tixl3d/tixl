# DrawVdbBounds

*in [Lib.volume.convert](README.md)*

Draws the sparse structure of a NanoVDB grid as wireframe boxes, one per node of the selected level.

Point Grid at the Buffer output of [LoadNanoVdb] or [VolumeToNanoVdb], and connect a [ComputeShader] whose Path is Lib:shaders/volume/nanovdb-bounds.hlsl. Draw the point output with [DrawLines].

Use it to verify a grid before trusting a render: Level 0 shows every leaf (the finest structure), Level 1 the lower nodes, Level 2 the upper nodes. If the boxes sit on top of the volume and their density matches what you expect, the tree, bounds and transform are being read correctly. If they are offset or scaled wrong, the grid's map is not the translation-and-voxel-size form these shaders assume.

AKA: vdb debug, nanovdb tree view, node bounds

Tips
- Capacity is in points, 36 per box; raise it when the boxes look clipped.
- MaxNodes bounds the dispatch; leaves past the grid's own count are skipped.
- A dense level set can have tens of thousands of leaves, so start with Level 2 to see the overall extent.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Capacity** (Int32) | Point slots in the output. Each box takes 36 slots (12 edges with a separator each). |
| **Color** (Vector4) | Line colour written into each point. |
| **Level** (Int32) | Which node level to outline: 0 = leaves (8 voxels), 1 = lower nodes (128), 2 = upper nodes (4096). |
| **MaxNodes** (Int32) | Number of nodes to dispatch. Larger than the grid is harmless; smaller clips the view. |

## Outputs

| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.BufferWithViews |


