# Procedural Geometry

TiXL can build and modify meshes on the CPU before turning them into GPU buffers for rendering: bevel a box, extrude text into solid 3D letters, displace a surface with noise, or shatter an object into chunks. This page introduces the geometry operator family and the chains you'll use most.

> [!NOTE]
> Procedural geometry is **experimental and a work in progress**. Operators, parameters and wire types may still change between releases, and some combinations are slow or not yet handled. If something breaks or feels awkward, please [report it](../getting-started/ReportBugs.md).

## CPU geometry vs. render meshes

Most mesh operators in TiXL work on **MeshBuffers** — packed GPU vertex and index buffers that are fast to draw but hard to restructure. The geometry operators work on CPU-side data instead:

- **Geometry** (`MeshGeometry`) — faces with any number of corners, shared points and flexible attributes. Quads stay quads, which is what makes modeling operations like beveling possible. Geometry can be split into **parts** (one per glyph, fracture chunk or copy), each with its own pivot.
- **Curves** — closed or open cubic-bezier outlines, for example the glyphs of a text.
- **Fields** — functions that return a value for any position in space, such as noise or the distance to a set of points. They don't hold data; operators like [DisplaceGeometry] sample them where they need them.

You can tell them apart by wire color: geometry and curves are teal-green, fields are a lighter yellow-green, and GPU mesh buffers stay red.

Geometry becomes renderable through [GeometryToMesh], which triangulates the faces and packs them for the GPU. Everything after it is a regular mesh.

## The basic chain

```
[CubeGeometry] -> [BevelGeometry] -> [GeometryToMesh] -> [DrawMesh]
```

- [CubeGeometry] generates a box with hard edges and per-face UVs. **Size** sets its dimensions.
- [BevelGeometry] rounds all edges — see below.
- [GeometryToMesh] compiles the result for rendering. Connect its output to any operator that accepts a mesh, such as [DrawMesh] or [TransformMesh].

[GeometryToMesh] only re-uploads when its input changes, so a static geometry chain costs nothing per frame.

## Beveling

[BevelGeometry] replaces every edge with a profile strip and every corner with a patch:

- **Width** — the size of the bevel in world units. It is limited to a share of the shortest edge so opposite bevels cannot cross; very large values stop growing at some point.
- **Segments** — how many faces make up each rounded edge. `1` gives a chamfer, higher values a smoother curve.
- **Roundness** — blends between a straight chamfer profile (`0`) and a circular arc (`1`).
- **RoundCorners** — builds rounded patches at the corners instead of flat fans. Needs more than one segment.
- **FlatShading** — by default bevels are smoothly shaded so the strips blend into the flat faces. Enable it for hard, faceted shading where every segment reads as its own plane.

Animating **Width** is cheap on simple shapes — the geometry rebuilds in real time.

## Transforming and inspecting

- [TransformGeometry] scales, rotates and translates geometry while it is still in CPU form. Use it before a bevel: beveling happens after the transform and adapts to stretched shapes.
- [CenterGeometry] moves geometry so a pivot inside its bounding box lands on the origin. Pivot `(0,-0.5,0)` puts the bottom on the ground plane — useful for imported files that aren't centered.
- [TriangulateGeometry] converts all faces into triangles. Rendering does this automatically, so you only need it when a later operation expects triangles.
- [GetGeometryStats] reports counts, bounds, volume and whether the shape is watertight. The output window also shows these stats when you view a geometry output directly.

## Importing geometry

[LoadObjGeometry] loads a Wavefront `.obj` file as geometry. Quads and other N-gons are kept, so imported models can be beveled. OBJ objects and groups become parts, and the file reloads automatically when it changes on disk. For glTF files and character animation, use [LoadGltfScene] instead — see [Character animation](CharacterAnimation.md).

## Deforming and coloring with fields

Fields describe a value that varies through space. Build one, optionally reshape it, then let an operator sample it:

```
[NoiseField] -> [RemapFieldValues] -> [DisplaceGeometry]
                     ^
             [GainAndBiasCurve]
```

- [NoiseField] — 3D noise with **Frequency**, **Octaves** and **Amplitude**. Animate **Offset** to make it flow.
- [DistanceToPointsField] — the distance to the closest point of a point list.
- [CustomScalarField] — write the field as a short C# expression using the position `p` and the parameters `A`–`D`. Compile errors appear in the log, and the last working version stays active while you type.
- [RemapFieldValues] with a [GainAndBiasCurve] shapes the values, the same way GainAndBias parameters do in the GPU operators.

[DisplaceGeometry] moves each point along its normal by the field value times **Amount**. Displacement needs enough points to show detail — a plain cube only has eight corners, so subdivide or bevel first.

[ColorFromField] writes a color attribute from a field. Note that the built-in mesh shaders don't display vertex colors yet.

## Text and curves

The curve operators turn fonts into 3D text:

```
[TextToCurves] -> [CurvesToGeometry] -> [GeometryToMesh] -> [DrawMesh]
```

- [TextToCurves] lays out text with a TrueType or OpenType font and outputs each glyph as a curve part. Variable fonts respond to **Weight** and one additional axis.
- [CurvesToGeometry] fills the outlines. With **Depth** above 0 it extrudes them into solid letters; **Bevel** and **BevelSegments** round or chamfer the edges. Keep the bevel below half the thinnest stroke, otherwise the surface folds over itself.
- [SubdivideCurves] splits long segments into shorter ones without changing the shape — useful before deforming outlines.
- [CurvesToPoints] flattens curves into a point list for [DrawLines] and the other point operators.

## Parts, fracture and chunks

Many operators produce geometry with several parts: one per glyph, OBJ group or fracture cell. Parts can be moved, filtered and colored individually.

```
[ScatterPointsInVolume] -> [VoronoiFracture] -> [ExplodeGeometry] -> [GeometryToMesh]
```

- [ScatterPointsInVolume] places a fixed number of points inside a box or a closed geometry. The same **Seed** always gives the same points.
- [VoronoiFracture] cuts the geometry into one cell per seed point. Cut faces are marked, so they can be styled separately from the outer surface.
- [SeparateLooseParts] splits geometry into one part per unconnected piece.
- [ExplodeGeometry] moves parts away from the center (**Distance**) and shrinks them (**Shrink**).
- [FilterGeoPartsByIndex] and [FilterGeoPartsInBox] keep only some parts — the quickest way to inspect a single chunk.
- [ColorFacesFromAttribute] picks a color per part or per face from a color list.

To animate parts on the GPU instead of rebuilding geometry every frame, compile them with [GeometryToMeshChunks]. It outputs one mesh with a chunk per part plus a point per pivot; move those points with any point operator and draw the result with [DrawMeshChunksAtPoints].

## Copying geometry to points

[PlaceGeometryAtPoints] copies a prototype geometry to every point of a CPU point list, using each point's position, orientation and scale. It works with any CPU point source, such as [ScatterPointsInVolume], [LineCPoints], [RadialCPoints], or GPU points read back with [ReadPointsFromGpu]. Each copy becomes a part, so the fracture and chunk operators above work on the result.

## Performance

Geometry operations run on the CPU, and some of them are expensive. [BevelGeometry] and [VoronoiFracture] have an **Async** option that computes the result on a background thread: the previous result stays visible until the new one is ready, and the computation restarts when parameters change. Enable it for heavy inputs so the editor stays responsive while you drag values.

Other things to keep in mind:

- Keep point and face counts moderate. Simple shapes rebuild in real time; detailed imported models or many fracture cells can take noticeably longer.
- Anything that doesn't change doesn't cost anything — results are cached until an input changes.
- For per-frame motion of many parts, prefer [GeometryToMeshChunks] with animated pivot points over animating the geometry itself.

## See also

- [Character animation](CharacterAnimation.md)
- [Real-time rendering](RealtimeRendering.md)
