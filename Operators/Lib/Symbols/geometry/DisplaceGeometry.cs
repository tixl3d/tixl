using System;
using System.Collections.Generic;

namespace Lib.geometry;

/// <summary>
/// Displaces geometry points along their geometric normals by a ScalarField sampled
/// at each point, scaled by Amount. If the input carries a Normal attribute, smooth
/// normals are recomputed from the displaced shape; otherwise the output stays
/// faceted like its input.
/// </summary>
[Guid("155cdda5-b4f0-4a9b-8ee5-fc4047bb7751")]
internal sealed class DisplaceGeometry : Instance<DisplaceGeometry>
{
    [Output(Guid = "3d41a807-3e7a-406c-aed8-7967e9712f90")]
    public readonly Slot<MeshGeometry> Result = new();

    public DisplaceGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var field = Field.GetValue(context);
        var amount = Amount.GetValue(context);

        if (source == null || field == null || amount == 0)
        {
            Result.Value = source;
            return;
        }

        var positions = source.Positions;
        var pointCount = positions.Length;

        // Geometric point normals of the input shape define the displacement directions
        AccumulatePointNormals(source, positions);

        if (_output.Positions.Length != pointCount)
            _output.Positions = new Vector3[pointCount];

        var evaluate = field.Evaluate;
        for (var i = 0; i < pointCount; i++)
        {
            var direction = _pointNormals[i];
            var length = direction.Length();
            if (length > 1e-10f)
                direction /= length;

            var sample = new FieldSample(positions[i]);
            _output.Positions[i] = positions[i] + direction * (evaluate(in sample) * amount);
        }

        // Topology and attributes are shared - except Normal, which displacement invalidates
        _output.FaceCornerOffsets = source.FaceCornerOffsets;
        _output.CornerPointIndices = source.CornerPointIndices;
        _output.Parts = source.Parts;
        _output.Attributes.Clear();
        var hadNormals = false;
        foreach (var attribute in source.Attributes)
        {
            if (string.Equals(attribute.Name, GeometryAttributeNames.Normal, StringComparison.OrdinalIgnoreCase))
                hadNormals = true;
            else
                _sharedAttributes.Add(attribute);
        }

        foreach (var shared in _sharedAttributes)
        {
            _output.Attributes.Add(shared);
        }

        _sharedAttributes.Clear();

        // A smooth-shaded input stays smooth: recompute point normals from the displaced shape
        if (hadNormals)
        {
            AccumulatePointNormals(_output, _output.Positions);
            var cornerPoints = _output.CornerPointIndices;
            var normals = _output.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, cornerPoints.Length);
            for (var c = 0; c < cornerPoints.Length; c++)
            {
                var normal = _pointNormals[cornerPoints[c]];
                var length = normal.Length();
                normals.Values[c] = length > 1e-10f ? normal / length : Vector3.UnitY;
            }
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    /// <summary>Accumulates Newell face normals onto points, into _pointNormals (unnormalized).</summary>
    private void AccumulatePointNormals(MeshGeometry geometry, Vector3[] positions)
    {
        var pointCount = positions.Length;
        if (_pointNormals.Length != pointCount)
            _pointNormals = new Vector3[pointCount];
        Array.Clear(_pointNormals, 0, pointCount);

        var offsets = geometry.FaceCornerOffsets;
        var cornerPoints = geometry.CornerPointIndices;
        for (var faceIndex = 0; faceIndex < geometry.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            var normal = Vector3.Zero;
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var p0 = positions[cornerPoints[c]];
                var p1 = positions[cornerPoints[next]];
                normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            for (var c = start; c < end; c++)
            {
                _pointNormals[cornerPoints[c]] += normal;
            }
        }
    }

    private readonly MeshGeometry _output = new();
    private readonly List<GeometryAttribute> _sharedAttributes = [];
    private Vector3[] _pointNormals = [];

    [Input(Guid = "a235cfd2-f2bd-4a0a-91e1-0cbcce9e2961")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "a90ce84d-ad07-4fe0-8cf8-3e3bd0c0240c")]
    public readonly InputSlot<ScalarField> Field = new();

    [Input(Guid = "efd37892-17d1-463e-a4a4-75584127b6e2")]
    public readonly InputSlot<float> Amount = new();
}
