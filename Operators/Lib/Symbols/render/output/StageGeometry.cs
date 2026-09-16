#nullable enable
using T3.Core.DataTypes;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// The active setup's physical surfaces as geometry in the stage: one quad per surface at its stage pose and
/// real size, so the room can be drawn, previewed and projected onto from the graph. TexCoord runs over each
/// surface; TexCoord2 is where the surface lands on an output's canvas, so texturing with that output's
/// composite (or its pixel map) puts every wall's picture on its wall.
/// </summary>
[Guid("98aac8ba-480c-4a28-8391-23c7d1ae11d9")]
internal sealed class StageGeometry : Instance<StageGeometry>
{
    [Output(Guid = "fcdaba8f-c63c-4913-b5d2-90b586ddc5f4", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<MeshGeometry> Geometry = new();

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
        var canvasOnTexCoord = CanvasUvOnTexCoord.GetValue(context);
        var count = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (IsStageSurface(surface))
                count++;
        }

        if (count == 0)
        {
            Geometry.Value = null;
            return;
        }

        EnsureTopology(count);
        var normals = _geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, count * 4);
        var texCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, count * 4);
        var canvasCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord2, AttributeDomain.Corner, count * 4);

        var index = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (!IsStageSurface(surface))
                continue;

            var pose = surface.Placement?.Pose ?? Pose.Identity;
            var world = pose.ToWorldMatrix();
            var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));

            // The surface's rect in its own space: X right, Y up, origin at the anchor.
            var min = -surface.AnchorInMeters;
            var max = min + surface.SizeInMeters;
            var mapping = outputId != Guid.Empty ? surface.FindMapping(outputId) : surface.OutputMappings.Count > 0 ? surface.OutputMappings[0] : null;
            var hasQuad = mapping is { Quad.Length: >= 4 };

            // Counter-clockwise seen from the front: bottom-left, bottom-right, top-right, top-left.
            for (var c = 0; c < 4; c++)
            {
                var corner = index * 4 + c;
                var local = _cornerOrder[c];
                _geometry.Positions[corner] = Vector3.Transform(new Vector3(local.X > 0 ? max.X : min.X, local.Y > 0 ? max.Y : min.Y, 0), world);
                normals.Values[corner] = normal;
                var canvasUv = hasQuad ? mapping!.Quad[_cornerToQuadIndex[c]] : _cornerUvs[c];
                texCoords.Values[corner] = canvasOnTexCoord ? canvasUv : _cornerUvs[c];
                canvasCoords.Values[corner] = canvasOnTexCoord ? _cornerUvs[c] : canvasUv;
            }

            _geometry.Parts[index] = new GeometryPart(index, 1, pose.Position, index, index);
            index++;
        }

        _geometry.InvalidateTopologyCaches();
        Geometry.Value = _geometry;
    }

    /// <summary>Roots only: a Layout region rides its parent's plane and is already part of that quad.</summary>
    private static bool IsStageSurface(Surface surface)
    {
        return surface.Kind == Surface.Kinds.Physical && surface.ParentId == Guid.Empty;
    }

    /** One detached quad per surface; rebuilt only when the surface count changes. */
    private void EnsureTopology(int count)
    {
        if (_geometry.Positions.Length == count * 4)
            return;

        _geometry.Positions = new Vector3[count * 4];
        _geometry.FaceCornerOffsets = new int[count + 1];
        _geometry.CornerPointIndices = new int[count * 4];
        _geometry.Parts = new GeometryPart[count];
        for (var i = 0; i < count; i++)
        {
            _geometry.FaceCornerOffsets[i] = i * 4;
            for (var c = 0; c < 4; c++)
                _geometry.CornerPointIndices[i * 4 + c] = i * 4 + c;
        }

        _geometry.FaceCornerOffsets[count] = count * 4;
        foreach (var name in _cornerAttributeNames)
        {
            if (_geometry.Attributes.TryGet<Vector3>(name, AttributeDomain.Corner, out var v3))
                v3.Resize(count * 4);
            else if (_geometry.Attributes.TryGet<Vector2>(name, AttributeDomain.Corner, out var v2))
                v2.Resize(count * 4);
        }
    }

    /** Corner order bottom-left, bottom-right, top-right, top-left as signs of the local axes. */
    private static readonly Vector2[] _cornerOrder = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];

    /** Image convention, Y down: the top-left corner is (0,0). */
    private static readonly Vector2[] _cornerUvs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];

    /** A mapping's quad is stored TL, TR, BR, BL; the corner order above reads it BL, BR, TR, TL. */
    private static readonly int[] _cornerToQuadIndex = [3, 2, 1, 0];

    private static readonly string[] _cornerAttributeNames = [GeometryAttributeNames.Normal, GeometryAttributeNames.TexCoord, GeometryAttributeNames.TexCoord2];

    private readonly MeshGeometry _geometry = new();

    [Input(Guid = "e64bc3c3-cbe2-4460-91a6-d0fc207c1743")]
    public readonly InputSlot<Guid> OutputRef = new();

    [Input(Guid = "b5066b38-41aa-4bbb-9d85-6f5bdf48c82d")]
    public readonly InputSlot<bool> CanvasUvOnTexCoord = new();
}
