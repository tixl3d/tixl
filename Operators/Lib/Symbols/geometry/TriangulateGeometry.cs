using System.Collections.Generic;

namespace Lib.geometry;

/// <summary>
/// Converts all N-gon faces into triangles (fan triangulation, assuming convex faces),
/// remapping corner attributes and part face-ranges. Rendering does this implicitly in
/// [GeometryToMeshBuffers]; use this op when downstream geometry ops need triangles.
/// </summary>
[Guid("593590aa-b2c6-42d7-b9c5-ce205ee4266d")]
internal sealed class TriangulateGeometry : Instance<TriangulateGeometry>
{
    [Output(Guid = "1a99482e-f4c2-4420-a3f5-393d398cc7bd")]
    public readonly Slot<MeshGeometry> Result = new();

    public TriangulateGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            return;
        }

        var triangleCount = source.GetTriangleCount();
        var cornerCount = triangleCount * 3;

        if (_output.FaceCornerOffsets.Length != triangleCount + 1)
            _output.FaceCornerOffsets = new int[triangleCount + 1];
        if (_output.CornerPointIndices.Length != cornerCount)
            _output.CornerPointIndices = new int[cornerCount];
        if (_cornerRemap.Length != cornerCount)
            _cornerRemap = new int[cornerCount];

        _output.Positions = source.Positions; // shared - points are unchanged

        var offsets = source.FaceCornerOffsets;
        var cornerPoints = source.CornerPointIndices;
        var faceTriangleStarts = _faceTriangleStarts;
        if (faceTriangleStarts.Length != source.FaceCount)
            faceTriangleStarts = _faceTriangleStarts = new int[source.FaceCount];

        var triangleIndex = 0;
        for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
        {
            faceTriangleStarts[faceIndex] = triangleIndex;
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var i = 1; i < end - start - 1; i++)
            {
                var outCorner = triangleIndex * 3;
                _output.FaceCornerOffsets[triangleIndex] = outCorner;
                _output.CornerPointIndices[outCorner] = cornerPoints[start];
                _output.CornerPointIndices[outCorner + 1] = cornerPoints[start + i];
                _output.CornerPointIndices[outCorner + 2] = cornerPoints[start + i + 1];
                _cornerRemap[outCorner] = start;
                _cornerRemap[outCorner + 1] = start + i;
                _cornerRemap[outCorner + 2] = start + i + 1;
                triangleIndex++;
            }
        }

        _output.FaceCornerOffsets[triangleCount] = cornerCount;

        // Corner attributes remap through the fan; other domains share buffers
        _output.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            if (attribute.Domain != AttributeDomain.Corner)
            {
                _sharedAttributes.Add(attribute);
                continue;
            }

            switch (attribute)
            {
                case GeometryAttribute<Vector2> v2:
                    Remap(v2.Values, _output.Attributes.GetOrCreate<Vector2>(attribute.Name, attribute.Domain, cornerCount).Values);
                    break;
                case GeometryAttribute<Vector3> v3:
                    Remap(v3.Values, _output.Attributes.GetOrCreate<Vector3>(attribute.Name, attribute.Domain, cornerCount).Values);
                    break;
                case GeometryAttribute<Vector4> v4:
                    Remap(v4.Values, _output.Attributes.GetOrCreate<Vector4>(attribute.Name, attribute.Domain, cornerCount).Values);
                    break;
                case GeometryAttribute<float> f1:
                    Remap(f1.Values, _output.Attributes.GetOrCreate<float>(attribute.Name, attribute.Domain, cornerCount).Values);
                    break;
                case GeometryAttribute<int> i1:
                    Remap(i1.Values, _output.Attributes.GetOrCreate<int>(attribute.Name, attribute.Domain, cornerCount).Values);
                    break;
            }
        }

        foreach (var shared in _sharedAttributes)
        {
            _output.Attributes.Add(shared);
        }

        _sharedAttributes.Clear();

        // Parts: face ranges become triangle ranges
        if (source.Parts.Length > 0)
        {
            if (_outputParts.Length != source.Parts.Length)
                _outputParts = new GeometryPart[source.Parts.Length];

            for (var i = 0; i < source.Parts.Length; i++)
            {
                var part = source.Parts[i];
                var firstTriangle = faceTriangleStarts[part.FaceStart];
                var endFace = part.FaceStart + part.FaceCount;
                var endTriangle = endFace < source.FaceCount ? faceTriangleStarts[endFace] : triangleCount;
                _outputParts[i] = part with { FaceStart = firstTriangle, FaceCount = endTriangle - firstTriangle };
            }

            _output.Parts = _outputParts;
        }
        else
        {
            _output.Parts = [];
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private void Remap<T>(T[] source, T[] target) where T : unmanaged
    {
        for (var i = 0; i < _cornerRemap.Length; i++)
        {
            target[i] = source[_cornerRemap[i]];
        }
    }

    private readonly MeshGeometry _output = new();
    private readonly List<GeometryAttribute> _sharedAttributes = [];
    private int[] _cornerRemap = [];
    private int[] _faceTriangleStarts = [];
    private GeometryPart[] _outputParts = [];

    [Input(Guid = "424f42c4-18f0-412a-a725-230700d5f1a6")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}
