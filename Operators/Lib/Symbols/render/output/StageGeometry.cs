#nullable enable
using T3.Core.DataTypes;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// The active setup's physical surfaces as geometry in the stage: one quad per surface at its stage pose and
/// real size — and for a floor that belongs to a floor plan, the plan's footprint itself, so an L-shaped room
/// gets an L-shaped floor. TexCoord follows UvMode; TexCoord2 is where the surface lands on an output's canvas,
/// so texturing with that output's composite (or its pixel map) puts every wall's picture on its wall.
/// </summary>
[Guid("98aac8ba-480c-4a28-8391-23c7d1ae11d9")]
internal sealed class StageGeometry : Instance<StageGeometry>
{
    [Output(Guid = "fcdaba8f-c63c-4913-b5d2-90b586ddc5f4", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<MeshGeometry?> Geometry = new();

    public StageGeometry()
    {
        Geometry.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var setup = ActiveSetup.Current;
        if (setup == null)
        {
            Geometry.Value = null;
            return;
        }

        var outputId = OutputRef.GetValue(context);
        var uvMode = (UvModes)UvMode.GetValue(context);
        var uvScale = MathF.Max(UvScale.GetValue(context), 0.001f);

        // Sizes first: each wall is a quad, a plan floor is its triangulated footprint.
        var pointCount = 0;
        var faceCount = 0;
        var cornerCount = 0;
        var partCount = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (!IsStageSurface(surface))
                continue;

            partCount++;
            var plan = FindClosedPlanFloor(setup, surface);
            if (plan != null)
            {
                plan.Triangulate(_triangles);
                pointCount += plan.Vertices.Count;
                faceCount += _triangles.Count / 3;
                cornerCount += _triangles.Count;
            }
            else
            {
                pointCount += 4;
                faceCount += 1;
                cornerCount += 4;
            }
        }

        if (faceCount == 0)
        {
            Geometry.Value = null;
            return;
        }

        EnsureCapacity(pointCount, faceCount, cornerCount, partCount);
        var normals = _geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, cornerCount);
        var texCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, cornerCount);
        var canvasCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord2, AttributeDomain.Corner, cornerCount);

        var point = 0;
        var face = 0;
        var corner = 0;
        var part = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (!IsStageSurface(surface))
                continue;

            // Where the surface lands on the canvas: its mapping on the output, else a patch of the same name on
            // that output — the pixel-map workflow, where patches are typed from the venue's sheet and named like
            // the walls — else its first mapping anywhere. Read into the TL, TR, BR, BL scratch either way.
            var hasCanvasQuad = TryGetCanvasQuad(setup, surface, outputId, _canvasQuad);
            var faceStart = face;
            var plan = FindClosedPlanFloor(setup, surface);
            if (plan != null)
            {
                // The footprint lies on the ground; its bounding box is the rectangle the surface (and its patch) stands for.
                plan.Triangulate(_triangles);
                plan.TryGetBounds(out var planMin, out var planMax);
                var planSize = Vector2.Max(planMax - planMin, new Vector2(0.0001f));
                var pointBase = point;
                for (var v = 0; v < plan.Vertices.Count; v++)
                    _geometry.Positions[point++] = PlanToStage(plan.Vertices[v]);

                for (var t = 0; t < _triangles.Count; t += 3)
                {
                    _geometry.FaceCornerOffsets[face++] = corner;
                    for (var k = 0; k < 3; k++)
                    {
                        var vertex = _triangles[t + k];
                        var inRect = (plan.Vertices[vertex] - planMin) / planSize; // 0..1, y away from the viewer
                        var position = _geometry.Positions[pointBase + vertex];
                        _geometry.CornerPointIndices[corner] = pointBase + vertex;
                        normals.Values[corner] = Vector3.UnitY;
                        var canvasUv = hasCanvasQuad ? BilinearCanvas(_canvasQuad, inRect.X, 1 - inRect.Y) : new Vector2(inRect.X, 1 - inRect.Y);
                        texCoords.Values[corner] = TexCoordFor(uvMode, uvScale, position, Vector3.UnitY, inRect, canvasUv);
                        canvasCoords.Values[corner] = canvasUv;
                        corner++;
                    }
                }
            }
            else
            {
                var pose = surface.Placement?.Pose ?? Pose.Identity;
                var world = pose.ToWorldMatrix();
                var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
                var min = -surface.AnchorInMeters;
                var max = min + surface.SizeInMeters;

                _geometry.FaceCornerOffsets[face++] = corner;
                // Counter-clockwise seen from the front: bottom-left, bottom-right, top-right, top-left.
                for (var c = 0; c < 4; c++)
                {
                    var sign = _cornerOrder[c];
                    var position = Vector3.Transform(new Vector3(sign.X > 0 ? max.X : min.X, sign.Y > 0 ? max.Y : min.Y, 0), world);
                    _geometry.Positions[point] = position;
                    _geometry.CornerPointIndices[corner] = point;
                    normals.Values[corner] = normal;
                    var inRect = new Vector2(sign.X > 0 ? 1 : 0, sign.Y > 0 ? 1 : 0);
                    var canvasUv = hasCanvasQuad ? _canvasQuad[_cornerToQuadIndex[c]] : new Vector2(inRect.X, 1 - inRect.Y);
                    texCoords.Values[corner] = TexCoordFor(uvMode, uvScale, position, normal, inRect, canvasUv);
                    canvasCoords.Values[corner] = canvasUv;
                    point++;
                    corner++;
                }
            }

            var pivot = _geometry.Positions[_geometry.CornerPointIndices[_geometry.FaceCornerOffsets[faceStart]]];
            _geometry.Parts[part] = new GeometryPart(faceStart, face - faceStart, pivot, part, part);
            part++;
        }

        _geometry.FaceCornerOffsets[face] = corner;
        _geometry.InvalidateTopologyCaches();
        Geometry.Value = _geometry;
    }

    /// <summary>
    /// TexCoord in the mesh shaders' convention (v up). Surface and canvas coordinates arrive v down and are
    /// flipped; the stage modes are written v up directly, so an image's top lands up on a wall and away on the floor.
    /// </summary>
    private static Vector2 TexCoordFor(UvModes mode, float scale, Vector3 position, Vector3 normal, Vector2 inRect, Vector2 canvasUv)
    {
        switch (mode)
        {
            case UvModes.OutputCanvas:
                return new Vector2(canvasUv.X, 1 - canvasUv.Y);

            case UvModes.StageTriplanar:
            {
                var absNormal = Vector3.Abs(normal);
                if (absNormal.X > absNormal.Y && absNormal.X > absNormal.Z)
                    return new Vector2(position.Z, position.Y) / scale;

                return absNormal.Y > absNormal.Z ? new Vector2(position.X, -position.Z) / scale : new Vector2(position.X, position.Y) / scale;
            }

            case UvModes.StageTopDown:
                return new Vector2(position.X, -position.Z) / scale;

            default:
                return inRect; // v up already: (0,0) is the surface's bottom-left
        }
    }

    /// <summary>A point inside the canvas quad (TL, TR, BR, BL) at fractions <paramref name="tx"/> across and <paramref name="ty"/> down.</summary>
    private static Vector2 BilinearCanvas(Vector2[] quad, float tx, float ty)
    {
        var top = quad[0] + (quad[1] - quad[0]) * tx;
        var bottom = quad[3] + (quad[2] - quad[3]) * tx;
        return top + (bottom - top) * ty;
    }

    private static bool TryGetCanvasQuad(Setup setup, Surface surface, Guid outputId, Vector2[] quad)
    {
        var mapping = outputId != Guid.Empty ? surface.FindMapping(outputId) : null;
        if (mapping is { Quad.Length: >= 4 })
        {
            Array.Copy(mapping.Quad, quad, 4);
            return true;
        }

        if (TryFindPatchQuadByName(setup, outputId, surface.Name, quad))
            return true;

        if (surface.OutputMappings.Count > 0 && surface.OutputMappings[0].Quad.Length >= 4)
        {
            Array.Copy(surface.OutputMappings[0].Quad, quad, 4);
            return true;
        }

        return false;
    }

    /// <summary>A patch on the output (or on any output when none is named) whose name is the surface's, in the turned corner order.</summary>
    private static bool TryFindPatchQuadByName(Setup setup, Guid outputId, string name, Vector2[] quad)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var output in setup.Outputs)
        {
            if (outputId != Guid.Empty && output.Id != outputId)
                continue;

            foreach (var patch in output.Patches)
            {
                if (patch.Quad.Length < 4 || !string.Equals(patch.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                patch.CopyTurnedCorners(quad);
                return true;
            }
        }

        return false;
    }

    /// <summary>The closed plan this surface is the floor of, if any; its footprint stands in for the surface's rectangle.</summary>
    private static FloorPlan? FindClosedPlanFloor(Setup setup, Surface surface)
    {
        foreach (var plan in setup.FloorPlans)
        {
            if (plan.IsClosed && plan.Vertices.Count >= 3 && plan.RaisedFloorId == surface.Id)
                return plan;
        }

        return null;
    }

    /// <summary>Plan metres to stage metres: the plan's up runs away from the viewer, on the ground.</summary>
    private static Vector3 PlanToStage(Vector2 point) => new(point.X, 0, -point.Y);

    /// <summary>Roots only: a Layout region rides its parent's plane and is already part of that quad.</summary>
    private static bool IsStageSurface(Surface surface)
    {
        return surface.Kind == Surface.Kinds.Physical && surface.ParentId == Guid.Empty;
    }

    /** Arrays sized for this frame's layout; reallocated only when a count changes. */
    private void EnsureCapacity(int pointCount, int faceCount, int cornerCount, int partCount)
    {
        if (_geometry.Positions.Length != pointCount)
            _geometry.Positions = new Vector3[pointCount];

        if (_geometry.FaceCornerOffsets.Length != faceCount + 1)
            _geometry.FaceCornerOffsets = new int[faceCount + 1];

        if (_geometry.CornerPointIndices.Length != cornerCount)
            _geometry.CornerPointIndices = new int[cornerCount];

        if (_geometry.Parts.Length != partCount)
            _geometry.Parts = new GeometryPart[partCount];

        foreach (var name in _cornerAttributeNames)
        {
            if (_geometry.Attributes.TryGet<Vector3>(name, AttributeDomain.Corner, out var v3) && v3.Count != cornerCount)
                v3.Resize(cornerCount);
            else if (_geometry.Attributes.TryGet<Vector2>(name, AttributeDomain.Corner, out var v2) && v2.Count != cornerCount)
                v2.Resize(cornerCount);
        }
    }

    /** Corner order bottom-left, bottom-right, top-right, top-left as signs of the local axes. */
    private static readonly Vector2[] _cornerOrder = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];

    /** A canvas quad is TL, TR, BR, BL; the corner order above reads it BL, BR, TR, TL. */
    private static readonly int[] _cornerToQuadIndex = [3, 2, 1, 0];

    private static readonly string[] _cornerAttributeNames = [GeometryAttributeNames.Normal, GeometryAttributeNames.TexCoord, GeometryAttributeNames.TexCoord2];

    private readonly MeshGeometry _geometry = new();
    private readonly List<int> _triangles = [];
    private readonly Vector2[] _canvasQuad = new Vector2[4];

    [Input(Guid = "e64bc3c3-cbe2-4460-91a6-d0fc207c1743")]
    public readonly InputSlot<Guid> OutputRef = new();

    [Input(Guid = "b5066b38-41aa-4bbb-9d85-6f5bdf48c82d", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> UvMode = new();

    [Input(Guid = "c1d2e3f4-5a6b-4c7d-8e9f-0a1b2c3d4e5f")]
    public readonly InputSlot<float> UvScale = new();

    /// <summary>What TexCoord carries; TexCoord2 always keeps the output-canvas coordinates, so one mesh serves the previz and [DrawStageCanvas] alike.</summary>
    private enum UvModes
    {
        /// <summary>0..1 over each surface, the image's top at the surface's top.</summary>
        Surface,

        /// <summary>Where the surface lands on the output canvas: textured with that output's composite or pixel map, every wall shows its own picture.</summary>
        OutputCanvas,

        /// <summary>The world position on each face's dominant plane, in metres over UvScale: a seamless material across walls and floor.</summary>
        StageTriplanar,

        /// <summary>The world position seen from above, in metres over UvScale: a floor plan image laid over the whole room.</summary>
        StageTopDown,
    }
}
