# SaveVdb

*in [Lib.volume.io](README.md)*

Writes the volume's grids to an OpenVDB file.

The file is readable by Houdini, Blender, Maya and TiXL. It is written once whenever Save switches from off to on. The write runs on a background thread, so large volumes do not stall the renderer.

AKA: export vdb, write openvdb

Tips
- Toggle Save from a button or trigger operator. The output pulses true for one evaluation after a successful write.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Save** (Boolean) | Writes the file on the rising edge. |
| **PathSaved** (String) | Target file path. Missing folders are created. |
| **ZipCompression** (Boolean) | Compress the voxel data with zip. Smaller files, slightly slower writing. |
| **Volume** (VdbVolume) | The volume to write. All grids of the volume are saved. |

## Outputs

| Name | Type |
|---|---|
| **Saved** | System.Boolean |


