# MeshToLaserPoints

*in [Lib.laser.convert](README.md)*

Converts a mesh into laser points — brings 3D mesh content into the laser pipeline as 2D vector graphics.

**Wireframe** mode emits each unique triangle edge as an isolated lit run (blanked approach point, then straight-line samples at `MaxEdgeStep`), so each edge becomes its own shape. **Vertices** mode emits each vertex as an isolated dwell dot (blanked jump + dwell run, matching [LaserCamera]'s dot encoding) so the mesh silhouette appears as a point cloud. Positions are projected through a Camera into ILDA 16-bit space; color comes from the vertex color multiplied by `Brightness`.

Consume the `LaserPoints` output through [LaserPointsToBuffer] into a point or line draw op for on-screen preview. To put the generated frame on a laser, draw the mesh into a [LaserCamera]'s **Scene** instead: buffers from [LaserPointsToBuffer] are excluded from scene capture so a laser frame never echoes itself. Optimization and safety run in the output setup, not in this operator.

## Input Parameters
| Name | Description |
|---|---|
| **Mesh** | Mesh to convert (any `MeshBuffers` source). Model-space positions; transform the mesh itself before this operator. |
| **Camera** | Camera used to project the mesh into laser space — any camera op, or a [LaserCamera] (its scanner camera then drives the projection). Use the same camera that renders the scene so the laser matches the audience view. |
| **Mode** | Wireframe: scan the triangle edges as connected lines (each edge blanked-separated, deduplicated). Vertices: each vertex as an isolated dwell dot. |
| **MaxEdgeStep** | Wireframe only: distance in 16-bit laser units between lit samples along an edge. Smaller = smoother/brighter lines but more points (default 4000 ≈ safety jump limit). |
| **DotDwellPoints** | Vertices mode only: lit repeats per dot (1..64). Stays under the output setup's static-dwell safety limiter. |
| **Brightness** | Global output brightness multiplier (0..1) applied on top of the vertex colors. |
| **UpdateContinuously** | When on, re-reads the mesh every frame (needed for animated meshes). Off = update only on TriggerUpdate or when settings change — cheaper for static meshes. |
| **TriggerUpdate** | Pulse to re-read the mesh from the GPU once on the next frame (ignored when UpdateContinuously is on). Resets to false automatically. |

## Outputs
| Name | Type |
|---|---|
| **LaserPoints** | StructuredList — ILDA 16-bit laser points |
| **PointCount** | Int32 — points in the frame |

## Notes

- Shared triangle edges are deduplicated so interior wireframe lines don't scan twice.
- Caps: 65536 unique edges, 256 lit samples per edge, 131072 vertices/faces, 1M output points — bigger meshes are truncated, not skipped.
- GPU readback uses the same copy-to-twin path as [LaserCamera]; it never creates buffers with initial data (uncatchable driver crashes).

**See also:** [LaserPointsToBuffer](LaserPointsToBuffer.md), [LaserCamera](../camera/LaserCamera.md)

---
*Generated from operator definition in Lib.laser.convert*
