using System;
using System.Collections.Generic;
using Lib.Utils;

namespace Lib.geometry;

/// <summary>
/// Keeps a range of parts by index: Start and Count. Count 1 isolates a single
/// chunk - the quickest way to inspect one fracture cell's triangulation. Geometry
/// without parts counts as one part.
/// </summary>
[Guid("e4a7c15d-9b23-4f68-8d0a-6c2e5b1f7a93")]
internal sealed class FilterGeoPartsByIndex : Instance<FilterGeoPartsByIndex>
{
    [Output(Guid = "9c3f2e81-a5d4-4b07-b6e1-38d0c7a9f256")]
    public readonly Slot<MeshGeometry> Result = new();

    [Output(Guid = "52b8d6f0-3a1e-4c79-9e4b-d7f1a0c3e582")]
    public readonly Slot<int> PartCount = new();

    public FilterGeoPartsByIndex()
    {
        Result.UpdateAction = Update;
        PartCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var start = Start.GetValue(context);
        var count = Count.GetValue(context);

        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            PartCount.Value = 0;
            return;
        }

        var parts = _subset.PartsOrWhole(source);
        PartCount.Value = parts.Length;

        // Negative Start counts from the end; Count <= 0 keeps everything from Start on
        if (start < 0)
            start = Math.Max(parts.Length + start, 0);
        var end = count <= 0 ? parts.Length : Math.Min(start + count, parts.Length);
        start = Math.Min(start, parts.Length);

        if (start == 0 && end == parts.Length)
        {
            Result.Value = source;
            return;
        }

        _keptParts.Clear();
        for (var i = start; i < end; i++)
        {
            _keptParts.Add(parts[i]);
        }

        _subset.Build(source, _keptParts, _output);
        Result.Value = _output;
    }

    private readonly MeshGeometry _output = new();
    private readonly GeometryPartSubset _subset = new();
    private readonly List<GeometryPart> _keptParts = [];

    [Input(Guid = "1d6f8a93-c2b5-4e40-a7d1-59e3b0c8f627")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "8e2c4b70-5f9a-4d16-b3c8-a0d7e1f5c394")]
    public readonly InputSlot<int> Start = new();

    [Input(Guid = "3b9e7d24-6a0f-4c85-9f2e-c1b6d8a4e057")]
    public readonly InputSlot<int> Count = new();
}
