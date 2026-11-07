#nullable enable
using T3.Core.DataTypes;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// The active setup's physical surfaces as geometry in the stage: one quad per surface at its stage pose and
/// real size — and for a floor that belongs to a floor plan, the plan's footprint itself, so an L-shaped room
/// gets an L-shaped floor. A region with a corner pin of its own is a quad too: it has its own place on a canvas,
/// which is how a stack of screens gets painted without its parent being mapped at all. TexCoord follows TexCoordSource; TexCoord2 is where the surface lands on an output's canvas,
/// so texturing with that output's composite (or its pixel map) puts every wall's picture on its wall.
/// </summary>
[Guid("98aac8ba-480c-4a28-8391-23c7d1ae11d9")]
internal sealed class StageGeometry : Instance<StageGeometry>, IStatusProvider
{
    [Output(Guid = "fcdaba8f-c63c-4913-b5d2-90b586ddc5f4")]
    public readonly Slot<MeshGeometry?> Geometry = new();

    public StageGeometry()
    {
        Geometry.UpdateAction = Update;

        // Re-derived only when the setup changes, so the mesh compiled downstream stays cached between edits.
        ActiveSetup.Changed += OnSetupChanged;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            ActiveSetup.Changed -= OnSetupChanged;

        base.Dispose(isDisposing);
    }

    private void OnSetupChanged() => Geometry.DirtyFlag.ForceInvalidate();

    private void Update(EvaluationContext context)
    {
        var setup = ActiveSetup.Current;
        if (setup == null)
        {
            Geometry.Value = null;
            return;
        }

        var canvasId = Canvas.GetValue(context);
        var texCoordSource = (TexCoordSources)TexCoordSource.GetValue(context);
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
                var cells = TryGetPhotoMap(setup, surface, texCoordSource, out var unusedMap) ? PhotoGridCells : 1;
                pointCount += 4 * cells * cells;
                faceCount += cells * cells;
                cornerCount += 4 * cells * cells;
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
        var placedCount = 0;
        var unplacedCount = 0;
        string? firstUnplacedName = null;
        foreach (var surface in setup.Surfaces)
        {
            if (!IsStageSurface(surface))
                continue;

            // Where the surface lands on the canvas: its mapping on the output, else a patch of the same name on
            // that output — the pixel-map workflow, where patches are typed from the venue's sheet and named like
            // the walls — else its first mapping anywhere. Read into the TL, TR, BR, BL scratch either way.
            var hasCanvasQuad = TryGetCanvasQuad(setup, surface, canvasId, _canvasQuad);
            if (hasCanvasQuad)
            {
                placedCount++;
            }
            else if (!HasRegionOnCanvas(setup, surface) && setup.FindSurface(canvasId) == null)
            {
                unplacedCount++;
                firstUnplacedName ??= surface.Name;
            }

            var faceStart = face;
            var hasPhoto = TryGetPhotoMap(setup, surface, texCoordSource, out var surfaceToPhoto);
            var plan = FindClosedPlanFloor(setup, surface);
            if (plan != null)
            {
                // The footprint lies on the ground; the plan's floor rectangle is what the surface (and its patch) stands for,
                // so a part of the footprint reaching past it, like a stage niche, gets coordinates beyond 0..1.
                plan.Triangulate(_triangles);
                plan.TryGetFloorRect(out var floorRect);
                var pointBase = point;
                for (var v = 0; v < plan.Vertices.Count; v++)
                    _geometry.Positions[point++] = PlanToStage(plan.Vertices[v]);

                for (var t = 0; t < _triangles.Count; t += 3)
                {
                    _geometry.FaceCornerOffsets[face++] = corner;
                    for (var k = 0; k < 3; k++)
                    {
                        var vertex = _triangles[t + k];
                        var inRect = floorRect.ToFractions(plan.Vertices[vertex]); // 0..1 inside the rectangle, y along its depth
                        var position = _geometry.Positions[pointBase + vertex];
                        _geometry.CornerPointIndices[corner] = pointBase + vertex;
                        normals.Values[corner] = Vector3.UnitY;
                        var canvasUv = hasCanvasQuad ? BilinearCanvas(_canvasQuad, inRect.X, 1 - inRect.Y) : new Vector2(inRect.X, 1 - inRect.Y);
                        texCoords.Values[corner] = hasPhoto
                                                       ? surfaceToPhoto.TransformPoint(inRect)
                                                       : TexCoordFor(texCoordSource, uvScale, position, Vector3.UnitY, inRect, canvasUv);
                        canvasCoords.Values[corner] = hasCanvasQuad ? canvasUv : OffCanvas;
                        corner++;
                    }
                }
            }
            else
            {
                T3.Core.Output.SurfaceGeometry.GetStageRect(setup, surface, out var world, out var min, out var max, out var lift);
                var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));

                // Texture coordinates interpolate linearly, a photo's perspective doesn't: a single quad would show a
                // kink along its diagonal, so a wall textured from its photo gets a grid fine enough to hide it.
                var cells = hasPhoto ? PhotoGridCells : 1;
                for (var row = 0; row < cells; row++)
                {
                    for (var column = 0; column < cells; column++)
                    {
                        _geometry.FaceCornerOffsets[face++] = corner;
                        // Counter-clockwise seen from the front: bottom-left, bottom-right, top-right, top-left.
                        for (var c = 0; c < 4; c++)
                        {
                            var sign = _cornerOrder[c];
                            var inRect = new Vector2((column + (sign.X > 0 ? 1f : 0f)) / cells, (row + (sign.Y > 0 ? 1f : 0f)) / cells);
                            var inSurface = min + (max - min) * inRect;
                            var position = Vector3.Transform(new Vector3(inSurface.X, inSurface.Y, lift), world);
                            _geometry.Positions[point] = position;
                            _geometry.CornerPointIndices[corner] = point;
                            normals.Values[corner] = normal;
                            var canvasUv = hasCanvasQuad ? BilinearCanvas(_canvasQuad, inRect.X, 1 - inRect.Y) : new Vector2(inRect.X, 1 - inRect.Y);
                            texCoords.Values[corner] = hasPhoto
                                                           ? surfaceToPhoto.TransformPoint(inRect)
                                                           : TexCoordFor(texCoordSource, uvScale, position, normal, inRect, canvasUv);
                            canvasCoords.Values[corner] = hasCanvasQuad ? canvasUv : OffCanvas;
                            point++;
                            corner++;
                        }
                    }
                }
            }

            var pivot = _geometry.Positions[_geometry.CornerPointIndices[_geometry.FaceCornerOffsets[faceStart]]];
            _geometry.Parts[part] = new GeometryPart(faceStart, face - faceStart, pivot, part, part);
            part++;
        }

        _geometry.FaceCornerOffsets[face] = corner;
        _geometry.InvalidateTopologyCaches();
        Geometry.Value = _geometry;
        UpdateUnplacedWarning(placedCount > 0 ? unplacedCount : 0, firstUnplacedName);
    }

    /** Rebuilt only when what it reports changes, so a steady setup costs no string per frame. */
    private void UpdateUnplacedWarning(int unplacedCount, string? firstUnplacedName)
    {
        if (unplacedCount == _unplacedCount && ReferenceEquals(firstUnplacedName, _firstUnplacedName))
            return;

        _unplacedCount = unplacedCount;
        _firstUnplacedName = firstUnplacedName;
        _unplacedWarning = unplacedCount == 0
                               ? null
                               : unplacedCount == 1
                                   ? $"'{firstUnplacedName}' has no patch of its name and no mapping on the output, so [ProjectOntoStage] leaves it out."
                                   : $"'{firstUnplacedName}' and {unplacedCount - 1} more surfaces have no patch of their name and no mapping on the output, so [ProjectOntoStage] leaves them out.";
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        _unplacedWarning == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string? IStatusProvider.GetStatusMessage() => _unplacedWarning;

    /// <summary>
    /// TexCoord in the mesh shaders' convention (v up). Surface and canvas coordinates arrive v down and are
    /// flipped; the stage modes are written v up directly, so an image's top lands up on a wall and away on the floor.
    /// </summary>
    private static Vector2 TexCoordFor(TexCoordSources mode, float scale, Vector3 position, Vector3 normal, Vector2 inRect, Vector2 canvasUv)
    {
        switch (mode)
        {
            case TexCoordSources.OutputCanvas:
                return new Vector2(canvasUv.X, 1 - canvasUv.Y);

            case TexCoordSources.StageTriplanar:
            {
                var absNormal = Vector3.Abs(normal);
                if (absNormal.X > absNormal.Y && absNormal.X > absNormal.Z)
                    return new Vector2(position.Z, position.Y) / scale;

                return absNormal.Y > absNormal.Z ? new Vector2(position.X, -position.Z) / scale : new Vector2(position.X, position.Y) / scale;
            }

            case TexCoordSources.StageTopDown:
                return new Vector2(position.X, -position.Z) / scale;

            case TexCoordSources.ReferenceImage:
                return OffCanvas; // only surfaces without a trace get here

            default:
                return inRect; // v up already: (0,0) is the surface's bottom-left
        }
    }

    /// <summary>
    /// In <see cref="TexCoordSources.ReferenceImage"/>, for a surface traced on an image: from 0..1 over the surface (v up)
    /// to where that spot is in the photo, in the mesh shaders' texture coordinates.
    /// </summary>
    private bool TryGetPhotoMap(Setup setup, Surface surface, TexCoordSources mode, out Homography surfaceToPhoto)
    {
        surfaceToPhoto = default;
        if (mode != TexCoordSources.ReferenceImage || surface.Trace is not { Quad.Length: >= 4 } trace)
            return false;

        var image = setup.FindReferenceImage(trace.ImageId);
        if (image == null || image.Width <= 0 || image.Height <= 0)
            return false;

        // Traced in photo pixels, y down.
        for (var i = 0; i < 4; i++)
            _photoQuad[i] = new Vector2(trace.Quad[i].X / image.Width, 1 - trace.Quad[i].Y / image.Height);

        return Homography.TryComputeQuadToQuad(_unitRectCorners, _photoQuad, out surfaceToPhoto);
    }

    /// <summary>A point inside the canvas quad (TL, TR, BR, BL) at fractions <paramref name="tx"/> across and <paramref name="ty"/> down.</summary>
    private static Vector2 BilinearCanvas(Vector2[] quad, float tx, float ty)
    {
        var top = quad[0] + (quad[1] - quad[0]) * tx;
        var bottom = quad[3] + (quad[2] - quad[3]) * tx;
        return top + (bottom - top) * ty;
    }

    internal static bool TryGetCanvasQuad(Setup setup, Surface surface, Guid outputId, Vector2[] quad)
    {
        if (setup.FindSurface(outputId) is { } layoutSurface)
            return TryGetQuadOnSurface(setup, surface, layoutSurface, quad);

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

    /// <summary>
    /// With a surface as the reference the canvas is that surface: the picture is laid out as the wall is, which
    /// is what slices cut from its regions expect. It and what lies on it land there; every other surface doesn't.
    /// </summary>
    private static bool TryGetQuadOnSurface(Setup setup, Surface surface, Surface layoutSurface, Vector2[] quad)
    {
        var min = -layoutSurface.AnchorInMeters;
        if (surface != layoutSurface)
        {
            min = surface.LocalPosition;
            var parent = setup.FindSurface(surface.ParentId);
            for (var guard = 0; parent != layoutSurface; guard++)
            {
                if (parent == null || guard >= 16)
                    return false;

                min += parent.LocalPosition + parent.AnchorInMeters;
                parent = setup.FindSurface(parent.ParentId);
            }
        }

        var layoutSize = Vector2.Max(layoutSurface.SizeInMeters, new Vector2(0.0001f));
        var layoutMin = -layoutSurface.AnchorInMeters;
        var left = (min.X - layoutMin.X) / layoutSize.X;
        var right = (min.X + surface.SizeInMeters.X - layoutMin.X) / layoutSize.X;
        var top = 1 - (min.Y + surface.SizeInMeters.Y - layoutMin.Y) / layoutSize.Y;
        var bottom = 1 - (min.Y - layoutMin.Y) / layoutSize.Y;
        quad[0] = new Vector2(left, top);
        quad[1] = new Vector2(right, top);
        quad[2] = new Vector2(right, bottom);
        quad[3] = new Vector2(left, bottom);
        return true;
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
    internal static bool IsStageSurface(Surface surface)
    {
        return surface.ParentId == Guid.Empty
                   ? surface.Kind == Surface.Kinds.Physical
                   : surface.Kind == Surface.Kinds.Layout && surface.OutputMappings.Count > 0;
    }


    /** A parent left off the canvas on purpose: its regions have the places, so there is nothing to warn about. */
    private static bool HasRegionOnCanvas(Setup setup, Surface parent)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId == parent.Id && IsStageSurface(surface))
                return true;
        }

        return false;
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

    private const int PhotoGridCells = 16;

    /** 0..1 over a surface (v up) in a traced quad's corner order: TL, TR, BR, BL. */
    private static readonly Vector2[] _unitRectCorners = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];

    private static readonly string[] _cornerAttributeNames = [GeometryAttributeNames.Normal, GeometryAttributeNames.TexCoord, GeometryAttributeNames.TexCoord2];

    private readonly MeshGeometry _geometry = new();
    private readonly List<int> _triangles = [];
    private readonly Vector2[] _canvasQuad = new Vector2[4];
    private readonly Vector2[] _photoQuad = new Vector2[4];

    /**
     * Where a surface with no patch or mapping lands on the canvas: one point outside it, so its triangles cover nothing.
     * Spread over the whole canvas instead, it would be painted over every other wall.
     */
    private static readonly Vector2 OffCanvas = new(-2, -2);

    private int _unplacedCount;
    private string? _firstUnplacedName;
    private string? _unplacedWarning;

    [Input(Guid = "e64bc3c3-cbe2-4460-91a6-d0fc207c1743")]
    public readonly InputSlot<Guid> Canvas = new();

    [Input(Guid = "b5066b38-41aa-4bbb-9d85-6f5bdf48c82d", MappedType = typeof(TexCoordSources))]
    public readonly InputSlot<int> TexCoordSource = new();

    [Input(Guid = "c1d2e3f4-5a6b-4c7d-8e9f-0a1b2c3d4e5f")]
    public readonly InputSlot<float> UvScale = new();

    /// <summary>What TexCoord carries; TexCoord2 always keeps the output-canvas coordinates, so one mesh serves the previz and [ProjectOntoStage] alike.</summary>
    private enum TexCoordSources
    {
        /// <summary>0..1 over each surface, the image's top at the surface's top.</summary>
        Surface,

        /// <summary>Where the surface lands on the output canvas: textured with that output's composite or pixel map, every wall shows its own picture.</summary>
        OutputCanvas,

        /// <summary>The world position on each face's dominant plane, in metres over UvScale: a seamless material across walls and floor.</summary>
        StageTriplanar,

        /// <summary>The world position seen from above, in metres over UvScale: a floor plan image laid over the whole room.</summary>
        StageTopDown,

        /// <summary>Where each surface is traced on its reference image: textured with that photo, every wall shows what it really looks like.</summary>
        ReferenceImage,
    }
}
