# ProceduralGeometry

TiXL can generate and modify meshes on the CPU before they are turned into GPU buffers for rendering. This page introduces the geometry operator family and the basic chain for building a beveled shape.

## CPU geometry vs. render meshes

Most mesh operators in TiXL work on **MeshBuffers** — packed GPU vertex and index buffers that are fast to draw but hard to restructure. The geometry operators work on a different type, **MeshGeometry**: a CPU-side format with faces that can have any number of corners, shared points, and flexible attributes. This makes modeling operations like beveling possible.

The two worlds connect through [GeometryToMeshBuffers], which triangulates the geometry and packs it for rendering. Wires carrying CPU geometry appear in a teal-green color, distinct from the red of GPU mesh buffers.

## The basic chain

```
[CubeGeometry] -> [BevelGeometry] -> [GeometryToMeshBuffers] -> [DrawMesh]
```

- **[CubeGeometry]** generates a box as procedural geometry, with hard edges and per-face UVs. The `Size` parameter sets its dimensions.
- **[BevelGeometry]** rounds all edges. See below.
- **[GeometryToMeshBuffers]** compiles the result for rendering. Connect its output to any operator that accepts a mesh, such as [DrawMesh] or [TransformMesh].

## Beveling

[BevelGeometry] replaces every edge with a profile strip and every corner with a rounded patch:

- **Width** — the size of the bevel. It is automatically limited so opposite bevels cannot cross, so very large values stop growing at some point.
- **Segments** — how many faces make up each rounded edge. `1` gives a chamfer, higher values a smoother curve.
- **Roundness** — blends between a straight chamfer profile (`0`) and a circular arc (`1`).
- **FlatShading** — by default bevels are smoothly shaded so the strips blend into the flat faces. Enable this for hard, faceted shading where every segment reads as its own plane.

Animating `Width` is cheap — the geometry rebuilds in real time.

## Other geometry operators

- **[TransformGeometry]** scales, rotates and translates geometry while it is still in CPU form — useful before a bevel, since beveling happens after the transform and adapts to stretched shapes.
- **[TriangulateGeometry]** converts all faces into triangles explicitly. Rendering does this automatically, so you only need it when a later geometry operation expects triangles. Beveling a triangulated shape still works: edges between flat triangles produce invisible flat strips, so only the real corners show bevels.

## See also

- [Realtime rendering](RealtimeRendering.md)
