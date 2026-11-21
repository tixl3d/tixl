# LoadNanoVdb

*in [Lib.volume.io](README.md)*

Loads a NanoVDB .nvdb file and uploads it as a GPU buffer.

NanoVDB is the flattened grid layout produced by nanovdb_convert from OpenVDB, and the one [SaveNanoVdb] writes. Nothing is parsed into a CPU volume, so a pre-converted cache goes straight to the renderer.

Path also accepts an animated cache pattern: cache_####.nvdb or cache_%04d.nvdb, with the frame number coming from Frame. Upcoming frames are read ahead into a shared 512 MB cache on a worker thread, so scrubbing does not hit the disk every step. The render thread uploads at most one new frame per evaluation and keeps showing the previous one until it is ready.

Outputs mirror [VolumeToNanoVdb]: feed ShaderResourceView to [NanoVdbRender] or [VdbToSdfField], and WorldMin/WorldMax frame the box. Use [LoadVdb] instead when you want to edit the volume.

AKA: load nvdb, import nanovdb, gpu volume, vdb sequence

Tips
- nanovdb_convert writes one grid per file; a multi-grid .vdb has to be split first.
- Only float grids have a renderer here; other value types will validate but render as garbage.
- Grids above 1 GB are rejected, because the upload copies the buffer once more.
- A frame file that is missing simply keeps the last good grid, so a partially rendered cache still plays.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Frame** (Int32) | Frame number for an animated cache. Ignored when Path has no #### or %04d token. |
| **Path** (String) | File path of the .nvdb file, or a sequence pattern such as cache_####.nvdb. |
| **PreloadFrames** (Int32) | Upcoming frames to read ahead into the shared cache. 0 disables prefetching. |

## Outputs

| Name | Type |
|---|---|
| **Buffer** | T3.Core.DataTypes.BufferWithViews |
| **ShaderResourceView** | T3.Graphics.Compat.ShaderResourceView |
| **TreeOffsets** | System.Numerics.Vector4 |
| **VoxelCount** | System.Single |
| **VoxelSize** | System.Single |
| **GridName** | System.String |
| **WorldMin** | System.Numerics.Vector3 |
| **WorldMax** | System.Numerics.Vector3 |


