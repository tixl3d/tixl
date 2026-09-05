using System;

namespace Lib.geometry;

/// <summary>
/// Separates the parts of a MeshGeometry visually: each part shrinks toward its
/// pivot and moves outward from the geometry center by Distance. Topology and
/// attributes are shared with the input.
/// </summary>
[Guid("c4f81d6a-95e2-4b37-a0d8-7e3b2c9f5061")]
internal sealed class ExplodeGeometry : Instance<ExplodeGeometry>
{
    [Output(Guid = "1e9c5b70-a4d8-4f26-b3e9-08d7a2c6f154")]
    public readonly Slot<MeshGeometry> Result = new();

    public ExplodeGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var distance = Distance.GetValue(context);
        var shrink = Math.Clamp(Shrink.GetValue(context), 0f, 1f);
        var autoCenter = AutoCenter.GetValue(context);
        var explicitCenter = Center.GetValue(context);

        if (source == null || source.Parts.Length == 0 || (distance == 0 && shrink == 0))
        {
            Result.Value = source;
            return;
        }

        var positions = source.Positions;
        if (_output.Positions.Length != positions.Length)
            _output.Positions = new Vector3[positions.Length];
        Array.Copy(positions, _output.Positions, positions.Length);

        // AutoCenter follows the mean pivot - which moves whenever parts are filtered
        // upstream; an explicit Center keeps the remaining parts where they were.
        var center = explicitCenter;
        if (autoCenter)
        {
            center = Vector3.Zero;
            foreach (var part in source.Parts)
            {
                center += part.Pivot;
            }

            center /= source.Parts.Length;
        }

        var offsets = source.FaceCornerOffsets;
        var corners = source.CornerPointIndices;
        foreach (var part in source.Parts)
        {
            var direction = part.Pivot - center;
            var length = direction.Length();
            var push = length > 1e-6f ? direction / length * distance : Vector3.Zero;

            var cornerStart = offsets[part.FaceStart];
            var cornerEnd = offsets[part.FaceStart + part.FaceCount];
            for (var c = cornerStart; c < cornerEnd; c++)
            {
                var pointId = corners[c];
                var p = positions[pointId];
                _output.Positions[pointId] = Vector3.Lerp(p, part.Pivot, shrink) + push;
            }
        }

        _output.FaceCornerOffsets = source.FaceCornerOffsets;
        _output.CornerPointIndices = source.CornerPointIndices;
        _output.Parts = source.Parts;
        _output.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            _output.Attributes.Add(attribute);
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private readonly MeshGeometry _output = new();

    [Input(Guid = "b7a03e58-c1f9-4d24-8b6a-52e0d9c7f183")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "60d2c9b4-8e57-4a01-bf3d-94a6e1c8d725")]
    public readonly InputSlot<float> Distance = new();

    [Input(Guid = "f3958a26-1d70-4c4b-a8e2-c65b0d493f17")]
    public readonly InputSlot<float> Shrink = new();

    [Input(Guid = "a7c2e9d4-5b18-4f06-93ea-0d6c1b8f2a57")]
    public readonly InputSlot<bool> AutoCenter = new();

    [Input(Guid = "3e8b0f61-c4a9-4d27-b5f0-72d9e1a6c483")]
    public readonly InputSlot<Vector3> Center = new();
}
