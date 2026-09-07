# PointsToRGBList

*in [Lib.io.dmx.obsolete](README.md)*

Converts a buffer of 3D points into a flat list of RGB float values (0-255).

This operator extracts the color (RGB) from each point in the input buffer and flattens it into a single list of floats, where each point contributes 3 float values (Red, Green, Blue). This can be useful for sending color data to systems that expect a linear list of values.

AKA: rgb, color, points, list, obsolete

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Collections.Generic.List`1[System.Single] |

