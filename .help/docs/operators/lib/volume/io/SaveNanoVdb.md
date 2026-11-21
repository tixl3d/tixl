# SaveNanoVdb

*in [Lib.volume.io](README.md)*

Writes the volume as a NanoVDB file.

NanoVDB is the GPU-flattened grid format used by CUDA and renderers, and is readable by the reference NanoVDB library. The file is written once whenever Save switches from off to on. Flattening and writing run on a background thread.

AKA: export nvdb, write nanovdb

Tips
- Only float and vector grids are written. The first float grid wins.
- Saved pulses true for one evaluation after the write has finished.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **PathSaved** (String) | Target file path. Missing folders are created. |
| **Volume** (VdbVolume) | The volume to write. |
| **Save** (Boolean) | Writes the file on the rising edge. |

## Outputs

| Name | Type |
|---|---|
| **Saved** | System.Boolean |


