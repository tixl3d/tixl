# MeshToLaserPoints

`Operators/Lib/Symbols/laser/convert/MeshToLaserPoints.cs`

Converts a mesh (`MeshBuffers` - anything from `TorusMesh`/`SphereMesh` to loaded models) into laser points. This brings 3D mesh content into the laser pipeline as 2D vector graphics, with the points/lines distinction encoded directly in the stream (same convention as [LaserCamera](LaserCamera.md)'s `Mode`).

## What it does

1. Reads the mesh's GPU vertex buffer (`PbrVertex`, positions at offset 0, `ColorRgb` for color) and index buffer (`Int3`, one triangle each) via CPU readback.
2. Projects positions through the connected camera (same math as [LaserCamera](LaserCamera.md): depth/|NDC| culling, clamp, ILDA 16-bit).
3. Encodes the geometry in `Mode`:
   * **Wireframe** - each *unique* triangle edge (shared edges deduplicated) becomes an isolated lit run: one blanked approach point, then straight-line samples every `MaxEdgeStep` units. Every edge is its own shape segment downstream.
   * **Vertices** - every vertex becomes an isolated dwell dot (blanked jump + `DotDwellPoints` lit repeats), so the mesh appears as a point cloud that is never connected by lit strokes.
4. Publishes the frame on **LaserPoints**; feed it into [Laser optimization](LaserOptimizer.md) like any other laser point list.

## Inputs

| Input | Default | Purpose |
|-------|---------|---------|
| `Mesh` | - | The `MeshBuffers` to convert |
| `Camera` | - | Projection camera; use the scene camera so the laser matches the view |
| `Mode` | Wireframe | `Wireframe` = edges as line strokes, `Vertices` = point cloud of dots |
| `MaxEdgeStep` | 4000 | Wireframe: distance (16-bit units) between lit samples along an edge |
| `DotDwellPoints` | 8 | Vertices mode: lit repeats per dot (1..64) |
| `Brightness` | 1 | Global multiplier on the vertex colors (0..1) |
| `TriggerUpdate` | - | Pulse to re-read the mesh once (auto-resets) |
| `UpdateContinuously` | off | Re-read every frame - needed for animated/deforming meshes |

## Outputs

| Output | Purpose |
|--------|---------|
| `LaserPoints` | The encoded frame (`StructuredList<LaserPoint>`) |
| `PointCount` | Output points, including blanked approaches and dwell repeats |

## Usage notes

* Meshes are read in **model space** - apply transforms to the mesh itself before this operator.
* Readback stalls the render thread briefly; for static meshes leave `UpdateContinuously` off and pulse `TriggerUpdate` instead.
* Caps keep pathological meshes from ballooning frames: 131072 vertices/faces read, 65536 unique edges, 256 samples per edge, 1M output points (log messages note when a cap engages).
* Wireframe color comes from the edge's start vertex `ColorRgb`; generators like `TorusMesh` set it to white - use `Brightness` for global dimming.
