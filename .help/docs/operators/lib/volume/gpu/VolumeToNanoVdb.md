# VolumeToNanoVdb

*in [Lib.volume.gpu](README.md)*

Flattens the volume into a NanoVDB buffer and uploads it to the GPU.

NanoVDB is the linear grid layout used by CUDA and GPU renderers. The buffer is uploaded as a structured uint buffer for custom HLSL traversal; the tree offsets are given relative to the start of the buffer.

AKA: GPU grid, linear grid, nvdb

Tips
- Use [SaveNanoVdb] to write the same layout to a file.

## Outputs

| Name | Type |
|---|---|
| **Buffer** | T3.Core.DataTypes.BufferWithViews |
| **ShaderResourceView** | T3.Graphics.Compat.ShaderResourceView |
| **TreeOffsets** | System.Numerics.Vector4 |
| **VoxelCount** | System.Single |
| **VoxelSize** | System.Single |
| **WorldMin** | System.Numerics.Vector3 |
| **WorldMax** | System.Numerics.Vector3 |


