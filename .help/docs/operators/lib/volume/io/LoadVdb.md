# LoadVdb

*in [Lib.volume.io](README.md)*

Loads grids from an OpenVDB file.

Reads uncompressed, zip and blosc-compressed data as written by Houdini, Blender or TiXL. Float grids become the primary data of the volume; vector grids such as velocity or color ride along in the same set. The file is reloaded when it changes and the operator is evaluated. Parsing runs on a background thread, so a large file does not stall the renderer; the output appears once the read has finished.

AKA: import vdb, read openvdb

Tips
- Leave GridName empty to load every supported grid.
- While a file is being read the previous value is kept; the output turns null only if the file is missing or invalid.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **GridName** (String) | Load only the grid with this name. Empty loads all supported grids. |
| **Path** (String) | File path of the .vdb file. |

## Outputs

| Name | Type |
|---|---|
| **Volume** | T3.Core.DataTypes.Volume.VdbVolume |


