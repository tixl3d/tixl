using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lib.geometry;

/// <summary>
/// Measures a MeshGeometry: counts, bounds, signed volume, watertightness and how
/// long pulling the input took. Stats are recomputed only when the geometry
/// changes; the Report string bundles everything for a text display.
/// </summary>
[Guid("3f9d7b21-8e64-4c05-a1b7-d5c2e8f0a693")]
internal sealed class GetGeometryStats : Instance<GetGeometryStats>
{
    [Output(Guid = "a1c4e7f2-5b93-4d60-8e2a-7f0d3b9c1e58")]
    public readonly Slot<string> Report = new();

    [Output(Guid = "5d2b8f16-c7a0-4e39-b4d1-2a9e6c0f7b84")]
    public readonly Slot<int> PointCount = new();

    [Output(Guid = "e8a3c5d9-04f1-4b72-9c6e-b3d7a2f8e015")]
    public readonly Slot<int> FaceCount = new();

    [Output(Guid = "7b6f0a2c-3d85-4e17-a9b4-c1e5d8f2a369")]
    public readonly Slot<int> TriangleCount = new();

    [Output(Guid = "c2d9e4b7-6a18-4f53-8b0c-e7a3f1d5c920")]
    public readonly Slot<int> PartCount = new();

    [Output(Guid = "94e1a6c3-b2d7-4f08-a5e9-0c6b3d8f7a41")]
    public readonly Slot<Vector3> BoundsMin = new();

    [Output(Guid = "1f7c3e9a-d5b0-4a64-b8c2-a4e0f6d9b273")]
    public readonly Slot<Vector3> BoundsMax = new();

    [Output(Guid = "68b2d0f4-a9e3-4c51-97d6-f2c8e1b5a307")]
    public readonly Slot<Vector3> Size = new();

    [Output(Guid = "d3a5f8c1-2e79-4b06-8f4a-c9e6b0d2a158")]
    public readonly Slot<float> Volume = new();

    [Output(Guid = "0e9b4c7d-f682-4a35-b1d8-5c3a7e2f9d06")]
    public readonly Slot<int> BoundaryEdges = new();

    [Output(Guid = "b5c8e2a0-7d41-4f96-a3b7-e1d9c4f6a825")]
    public readonly Slot<float> EvaluationMs = new();

    public GetGeometryStats()
    {
        Report.UpdateAction = Update;
        PointCount.UpdateAction = Update;
        FaceCount.UpdateAction = Update;
        TriangleCount.UpdateAction = Update;
        PartCount.UpdateAction = Update;
        BoundsMin.UpdateAction = Update;
        BoundsMax.UpdateAction = Update;
        Size.UpdateAction = Update;
        Volume.UpdateAction = Update;
        BoundaryEdges.UpdateAction = Update;
        EvaluationMs.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var start = Stopwatch.GetTimestamp();
        var geometry = Geometry.GetValue(context);
        var evaluationMs = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

        if (geometry == null)
        {
            Report.Value = "No geometry";
            PointCount.Value = FaceCount.Value = TriangleCount.Value = PartCount.Value = BoundaryEdges.Value = 0;
            BoundsMin.Value = BoundsMax.Value = Size.Value = Vector3.Zero;
            Volume.Value = 0;
            EvaluationMs.Value = evaluationMs;
            return;
        }

        // Upstream evaluation only costs something when the geometry actually changed
        if (geometry != _lastGeometry || geometry.Version != _lastVersion)
        {
            Measure(geometry);
            _lastGeometry = geometry;
            _lastVersion = geometry.Version;
            _lastEvaluationMs = evaluationMs;
        }

        EvaluationMs.Value = _lastEvaluationMs;
        Report.Value = $"""
                        Points: {PointCount.Value}   Faces: {FaceCount.Value}   Triangles: {TriangleCount.Value}   Parts: {PartCount.Value}
                        Size: {Size.Value.X:0.###} x {Size.Value.Y:0.###} x {Size.Value.Z:0.###}
                        Bounds: ({BoundsMin.Value.X:0.###}, {BoundsMin.Value.Y:0.###}, {BoundsMin.Value.Z:0.###}) .. ({BoundsMax.Value.X:0.###}, {BoundsMax.Value.Y:0.###}, {BoundsMax.Value.Z:0.###})
                        Volume: {Volume.Value:0.####}   Boundary edges: {BoundaryEdges.Value}{(BoundaryEdges.Value == 0 ? " (watertight)" : "")}
                        Attributes: {_attributeSummary}
                        Evaluation: {_lastEvaluationMs:0.#} ms
                        """;
    }

    private void Measure(MeshGeometry geometry)
    {
        _stats.Measure(geometry);

        _attributeParts.Clear();
        foreach (var attribute in geometry.Attributes)
        {
            _attributeParts.Add($"{attribute.Name}@{attribute.Domain}");
        }

        _attributeSummary = _attributeParts.Count == 0 ? "none" : string.Join(", ", _attributeParts);

        PointCount.Value = _stats.PointCount;
        FaceCount.Value = _stats.FaceCount;
        TriangleCount.Value = _stats.TriangleCount;
        PartCount.Value = _stats.PartCount;
        BoundsMin.Value = _stats.BoundsMin;
        BoundsMax.Value = _stats.BoundsMax;
        Size.Value = _stats.Size;
        Volume.Value = _stats.Volume;
        BoundaryEdges.Value = _stats.BoundaryEdges;
    }

    private readonly MeshGeometryStats _stats = new();
    private readonly List<string> _attributeParts = [];
    private string _attributeSummary = "none";
    private MeshGeometry? _lastGeometry;
    private int _lastVersion;
    private float _lastEvaluationMs;

    [Input(Guid = "4a8e2c6f-d1b3-4f70-9e5d-b7c0a3f8e214")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}
