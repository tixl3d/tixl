using System;

namespace Lib.geometry;

/// <summary>
/// Moves geometry so a normalized pivot inside its bounding box lands on the world
/// origin. Pivot (0,0,0) centers the bounding box; (0,-0.5,0) puts the bottom
/// center on the ground - handy for OBJ files that aren't centered.
/// </summary>
[Guid("e93a5c07-4b86-4d21-a7f9-08c6d2e5b134")]
internal sealed class CenterGeometry : Instance<CenterGeometry>
{
    [Output(Guid = "6b0f8e24-d951-4c73-9ea0-42d7c8a3f516")]
    public readonly Slot<MeshGeometry> Result = new();

    public CenterGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var pivot = Pivot.GetValue(context);

        if (source == null || source.Positions.Length == 0)
        {
            Result.Value = source;
            return;
        }

        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);
        foreach (var p in source.Positions)
        {
            boundsMin = Vector3.Min(boundsMin, p);
            boundsMax = Vector3.Max(boundsMax, p);
        }

        var size = boundsMax - boundsMin;
        var pivotPoint = (boundsMin + boundsMax) * 0.5f + pivot * size;

        var positions = source.Positions;
        if (_output.Positions.Length != positions.Length)
            _output.Positions = new Vector3[positions.Length];

        for (var i = 0; i < positions.Length; i++)
        {
            _output.Positions[i] = positions[i] - pivotPoint;
        }

        // Pure translation: topology, attributes and normals are shared unchanged
        _output.FaceCornerOffsets = source.FaceCornerOffsets;
        _output.CornerPointIndices = source.CornerPointIndices;

        // Part pivots are world positions and must move along
        if (_output.Parts.Length != source.Parts.Length)
            _output.Parts = new GeometryPart[source.Parts.Length];
        for (var i = 0; i < source.Parts.Length; i++)
        {
            _output.Parts[i] = source.Parts[i] with { Pivot = source.Parts[i].Pivot - pivotPoint };
        }
        _output.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            _output.Attributes.Add(attribute);
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private readonly MeshGeometry _output = new();

    [Input(Guid = "27c5b9e1-84f0-4a63-b2d8-59e6c0d4a713")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "b40e7d92-3a58-4c16-9f27-06b8e5c1d349")]
    public readonly InputSlot<Vector3> Pivot = new();
}
