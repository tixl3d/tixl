# VolumeInfo

*in [Lib.volume.analyze](README.md)*

Reports statistics about a volume.

Active voxel count, memory footprint, value range, average and NaN or Inf corruption. Use it to catch broken volumes early in a graph.

AKA: stats, diagnose, inspect

Tips
- Enable LogToConsole once to see a full line with leaf count and value range in the log.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Volume** (VdbVolume) | The volume to inspect. |
| **LogToConsole** (Boolean) | Write a summary line to the log whenever the volume changes. |

## Outputs

| Name | Type |
|---|---|
| **ActiveVoxels** | System.Single |
| **MemoryMB** | System.Single |
| **HasNaN** | System.Boolean |
| **Min** | System.Single |
| **Max** | System.Single |
| **Average** | System.Single |


