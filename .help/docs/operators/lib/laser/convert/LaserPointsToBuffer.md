# LaserPointsToBuffer

*in [Lib.laser.convert](README.md)*

Converts a laser point list (`StructuredList<LaserPoint>`) into a GPU structured buffer (`BufferWithViews`) with the standard `Point` layout — the bridge from laser frames back into the graph.

LaserPoint X/Y are scaled to world-space positions; R/G/B/I become the Color vector. Use the buffer to preview received or generated frames on screen with a point or line draw op ([DrawPoints2], [DrawLines]). It is not a route back into a [LaserCamera]: buffers from this operator are excluded from scene capture so a laser frame never echoes itself.

## Input Parameters
| Name | Description |
|---|---|
| **LaserPoints** | Laser point list to convert. Blanked points (`I = 0`) are preserved as zero-alpha entries. |
| **Scale** | Multiplier applied to each X/Y coordinate. Default 1/32767 maps the ILDA 16-bit signed range to roughly [-1, 1]. Use 1 for raw integer units. |

## Outputs
| Name | Type |
|---|---|
| **Buffer** | BufferWithViews — structured `Point` buffer |
| **PointCount** | Int32 — points converted this frame |

## Notes

- The GPU upload is exact-size: the buffer allocation always matches the point count, avoiding driver access violations from oversized uploads.
- A hard cap of 1M points bounds damage from corrupt input (e.g. malformed network frames).

**See also:** [MeshToLaserPoints](MeshToLaserPoints.md), [PONKInput](../input/PONKInput.md), [LaserCamera](../camera/LaserCamera.md)

---
*Generated from operator definition in Lib.laser.convert*
