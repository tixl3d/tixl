using System;

namespace Lib.geometry;

/// <summary>
/// CPU instancing: copies a prototype geometry to every point, one part per point.
/// The point's position, orientation and scale place the copy; its color becomes a
/// part attribute and its index is kept as the SourcePoint part attribute. Separator
/// points are skipped. Prototype parts are flattened into the instance.
/// </summary>
[Guid("8d5f3a27-b9c4-4e61-a0d2-6f7e1c8b3a95")]
internal sealed class PlaceGeometryAtPoints : Instance<PlaceGeometryAtPoints>
{
    [Output(Guid = "4b7e2d90-6c1a-4f58-9e3b-d2a8f5c7e014")]
    public readonly Slot<MeshGeometry> Result = new();

    [Output(Guid = "a9c1e5f3-2d84-4b06-8f7a-1e6d3c9b5a28")]
    public readonly Slot<int> InstanceCount = new();

    public PlaceGeometryAtPoints()
    {
        Result.UpdateAction = Update;
        InstanceCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var prototype = Geometry.GetValue(context);
        var points = Points.GetValue(context) as StructuredList<Point>;
        var useOrientation = UseOrientation.GetValue(context);
        var useScale = UseScale.GetValue(context);
        var useColor = UseColor.GetValue(context);

        if (prototype == null || prototype.FaceCount == 0 || points == null || points.NumElements == 0)
        {
            Result.Value = null;
            InstanceCount.Value = 0;
            return;
        }

        var sourcePoints = points.TypedElements;
        var instanceCount = 0;
        for (var i = 0; i < sourcePoints.Length; i++)
        {
            if (!Point.IsSeparator(sourcePoints[i]))
                instanceCount++;
        }

        InstanceCount.Value = instanceCount;
        if (instanceCount == 0)
        {
            Result.Value = null;
            return;
        }

        var protoPositions = prototype.Positions;
        var protoOffsets = prototype.FaceCornerOffsets;
        var protoCorners = prototype.CornerPointIndices;
        var protoPointCount = protoPositions.Length;
        var protoFaceCount = prototype.FaceCount;
        var protoCornerCount = prototype.CornerCount;

        var positions = _output.Positions;
        if (positions.Length != protoPointCount * instanceCount)
            positions = new Vector3[protoPointCount * instanceCount];

        var offsets = _output.FaceCornerOffsets;
        if (offsets.Length != protoFaceCount * instanceCount + 1)
            offsets = new int[protoFaceCount * instanceCount + 1];

        var corners = _output.CornerPointIndices;
        if (corners.Length != protoCornerCount * instanceCount)
            corners = new int[protoCornerCount * instanceCount];

        var parts = new GeometryPart[instanceCount];

        _output.Attributes.Clear();
        prototype.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var protoNormals);
        var normals = protoNormals != null
                          ? _output.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, corners.Length)
                          : null;
        var colors = useColor
                         ? _output.Attributes.GetOrCreate<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Part, instanceCount)
                         : null;
        var sourceIndices = _output.Attributes.GetOrCreate<int>(GeometryAttributeNames.SourcePoint, AttributeDomain.Part, instanceCount);

        var instance = 0;
        for (var pointIndex = 0; pointIndex < sourcePoints.Length; pointIndex++)
        {
            ref readonly var point = ref sourcePoints[pointIndex];
            if (Point.IsSeparator(point))
                continue;

            var rotation = useOrientation ? point.Orientation : Quaternion.Identity;
            var scale = useScale ? point.Scale : Vector3.One;
            var pointBase = instance * protoPointCount;
            var faceBase = instance * protoFaceCount;
            var cornerBase = instance * protoCornerCount;

            for (var p = 0; p < protoPointCount; p++)
            {
                positions[pointBase + p] = Vector3.Transform(protoPositions[p] * scale, rotation) + point.Position;
            }

            for (var f = 0; f < protoFaceCount; f++)
            {
                offsets[faceBase + f] = cornerBase + protoOffsets[f];
            }

            for (var c = 0; c < protoCornerCount; c++)
            {
                corners[cornerBase + c] = pointBase + protoCorners[c];
            }

            if (normals != null)
            {
                // Non-uniform scale bends normals by the inverse scale before rotating
                var normalScale = new Vector3(1f / MathF.Max(MathF.Abs(scale.X), 1e-6f),
                                              1f / MathF.Max(MathF.Abs(scale.Y), 1e-6f),
                                              1f / MathF.Max(MathF.Abs(scale.Z), 1e-6f));
                for (var c = 0; c < protoCornerCount; c++)
                {
                    var n = Vector3.Transform(protoNormals!.Values[c] * normalScale, rotation);
                    var length = n.Length();
                    normals.Values[cornerBase + c] = length > 1e-8f ? n / length : n;
                }
            }

            if (colors != null)
                colors.Values[instance] = point.Color;

            sourceIndices.Values[instance] = pointIndex;
            parts[instance] = new GeometryPart(faceBase, protoFaceCount, point.Position, instance, pointIndex);
            instance++;
        }

        offsets[protoFaceCount * instanceCount] = corners.Length;

        // Remaining prototype attributes repeat unchanged per instance (normals were handled above, parts are flattened)
        foreach (var attribute in prototype.Attributes)
        {
            if (attribute.Domain == AttributeDomain.Part || ReferenceEquals(attribute, protoNormals))
                continue;

            var domainCount = attribute.Domain switch
                                  {
                                      AttributeDomain.Point  => protoPointCount,
                                      AttributeDomain.Corner => protoCornerCount,
                                      AttributeDomain.Face   => protoFaceCount,
                                      _                      => 0,
                                  };
            if (domainCount == 0)
                continue;

            switch (attribute)
            {
                case GeometryAttribute<float> a:
                    Repeat(a.Values, _output.Attributes.GetOrCreate<float>(a.Name, a.Domain, domainCount * instanceCount).Values, instanceCount);
                    break;
                case GeometryAttribute<int> a:
                    Repeat(a.Values, _output.Attributes.GetOrCreate<int>(a.Name, a.Domain, domainCount * instanceCount).Values, instanceCount);
                    break;
                case GeometryAttribute<Vector2> a:
                    Repeat(a.Values, _output.Attributes.GetOrCreate<Vector2>(a.Name, a.Domain, domainCount * instanceCount).Values, instanceCount);
                    break;
                case GeometryAttribute<Vector3> a:
                    Repeat(a.Values, _output.Attributes.GetOrCreate<Vector3>(a.Name, a.Domain, domainCount * instanceCount).Values, instanceCount);
                    break;
                case GeometryAttribute<Vector4> a:
                    Repeat(a.Values, _output.Attributes.GetOrCreate<Vector4>(a.Name, a.Domain, domainCount * instanceCount).Values, instanceCount);
                    break;
            }
        }

        _output.Positions = positions;
        _output.FaceCornerOffsets = offsets;
        _output.CornerPointIndices = corners;
        _output.Parts = parts;
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private static void Repeat<T>(T[] source, T[] target, int times) where T : unmanaged
    {
        for (var i = 0; i < times; i++)
        {
            Array.Copy(source, 0, target, i * source.Length, source.Length);
        }
    }

    private readonly MeshGeometry _output = new();

    [Input(Guid = "2c9e7b41-d5a3-4f80-b6e1-8a0f3d7c2e59")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "f1a6d3c8-4e27-4b95-a0d7-c3b9e5f8a162")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "7e3b9f52-a8c1-4d06-9b4e-d6f2a1c7e380")]
    public readonly InputSlot<bool> UseOrientation = new();

    [Input(Guid = "b4d8e2a7-3f19-4c65-8e0a-1c7f5b9d3a46")]
    public readonly InputSlot<bool> UseScale = new();

    [Input(Guid = "5a1f7c3e-9d62-4b08-a3e5-f0b8d4c6e271")]
    public readonly InputSlot<bool> UseColor = new();
}
