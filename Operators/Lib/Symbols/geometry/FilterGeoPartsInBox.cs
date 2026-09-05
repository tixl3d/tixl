using System;
using System.Collections.Generic;
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Keeps or discards the parts of a MeshGeometry by whether their pivot lies inside
/// a box volume. Handy for isolating a slice of fracture chunks for inspection, or
/// for culling chunks outside an area. Geometry without parts counts as one part.
/// </summary>
[Guid("b81f6c2d-4a93-4e07-9d5e-3c7a0f1b8e64")]
internal sealed class FilterGeoPartsInBox : Instance<FilterGeoPartsInBox>, ITransformable
{
    [Output(Guid = "2e7c9a54-d106-4b38-8f1a-c5d3e0b7a921")]
    public readonly TransformCallbackSlot<MeshGeometry> Result = new();

    [Output(Guid = "7a3d5f80-1c62-4e9b-b0d4-96e8a2c1f375")]
    public readonly Slot<int> KeptCount = new();

    public FilterGeoPartsInBox()
    {
        Result.TransformableOp = this;
        Result.UpdateAction = Update;
        KeptCount.UpdateAction = Update;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => Size;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var center = Center.GetValue(context);
        var size = Size.GetValue(context);
        var keepInside = (Modes)Mode.GetValue(context).Clamp(0, 1) == Modes.KeepInside;

        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            KeptCount.Value = 0;
            return;
        }

        var boxMin = center - size * 0.5f;
        var boxMax = center + size * 0.5f;
        var parts = _subset.PartsOrWhole(source);

        _keptParts.Clear();
        foreach (var part in parts)
        {
            var pivot = part.Pivot;
            var inside = pivot.X >= boxMin.X && pivot.X <= boxMax.X
                         && pivot.Y >= boxMin.Y && pivot.Y <= boxMax.Y
                         && pivot.Z >= boxMin.Z && pivot.Z <= boxMax.Z;
            if (inside == keepInside)
                _keptParts.Add(part);
        }

        KeptCount.Value = _keptParts.Count;
        if (_keptParts.Count == parts.Length)
        {
            Result.Value = source;
            return;
        }

        _subset.Build(source, _keptParts, _output);
        Result.Value = _output;
    }

    private enum Modes
    {
        KeepInside,
        KeepOutside,
    }

    private readonly MeshGeometry _output = new();
    private readonly GeometryPartSubset _subset = new();
    private readonly List<GeometryPart> _keptParts = [];

    [Input(Guid = "0c5e8a72-6d14-4f9b-a3e7-b18d2c6f0459")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "d9a2f6b1-3e58-4c07-8f4d-52c0e7a9b136")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "6b4e0d97-a25c-4f83-91c6-e7d3a8f2b504")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "f27c1e58-8b0d-4a96-b5f3-04e9d6c1a782", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
}
