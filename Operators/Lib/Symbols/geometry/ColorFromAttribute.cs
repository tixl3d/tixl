using System;
using System.Collections.Generic;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Colors faces from an integer-like attribute by picking from a color list: the
/// part index or seed index (one color per fracture chunk), or a named face
/// attribute. Only selected faces are touched by default, so cut faces of a
/// fracture can be tinted per chunk while the original surface keeps its color.
/// </summary>
[Guid("5f2a7c19-83d4-4e6b-a0c8-b7e1d9f3a256")]
internal sealed class ColorFromAttribute : Instance<ColorFromAttribute>
{
    [Output(Guid = "c81e4b70-2f95-4d3a-96b1-e0d7a5c2f849")]
    public readonly Slot<MeshGeometry> Result = new();

    public ColorFromAttribute()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var colors = Colors.GetValue(context);
        var attributeSource = (Sources)Source.GetValue(context).Clamp(0, 2);
        var attributeName = AttributeName.GetValue(context);
        var wrap = (WrapModes)Wrap.GetValue(context).Clamp(0, 1);
        var onlySelected = OnlySelected.GetValue(context);

        if (source == null || source.FaceCount == 0 || colors == null || colors.Count == 0)
        {
            Result.Value = source;
            return;
        }

        var offsets = source.FaceCornerOffsets;
        var cornerCount = source.CornerCount;

        // Start from the existing corner colors so upstream coloring survives on untouched faces
        var output = _output.Attributes.GetOrCreate<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Corner, cornerCount);
        if (source.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Corner, out var existing))
            Array.Copy(existing.Values, output.Values, cornerCount);
        else
            Array.Fill(output.Values, Vector4.One);

        GeometryAttribute<float>? selection = null;
        if (onlySelected)
            source.Attributes.TryGet(GeometryAttributeNames.Selection, AttributeDomain.Face, out selection);

        GeometryAttribute<float>? faceValues = null;
        if (attributeSource == Sources.FaceAttribute)
            source.Attributes.TryGet(attributeName, AttributeDomain.Face, out faceValues);

        BuildFaceToPart(source);

        for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
        {
            if (selection != null && selection.Values[faceIndex] < 0.5f)
                continue;

            int index;
            switch (attributeSource)
            {
                case Sources.PartIndex:
                    index = _faceToPart[faceIndex];
                    break;
                case Sources.PartSeedIndex:
                    index = _faceToPart[faceIndex] >= 0 ? source.Parts[_faceToPart[faceIndex]].SeedIndex : -1;
                    break;
                default:
                    index = faceValues != null ? (int)MathF.Round(faceValues.Values[faceIndex]) : -1;
                    break;
            }

            if (index < 0)
                continue;

            var colorIndex = wrap == WrapModes.Repeat
                                 ? index % colors.Count
                                 : Math.Min(index, colors.Count - 1);
            var color = colors[colorIndex];
            for (var c = offsets[faceIndex]; c < offsets[faceIndex + 1]; c++)
            {
                output.Values[c] = color;
            }
        }

        // Share everything but the Color attribute
        _output.Positions = source.Positions;
        _output.FaceCornerOffsets = source.FaceCornerOffsets;
        _output.CornerPointIndices = source.CornerPointIndices;
        _output.Parts = source.Parts;
        _shared.Clear();
        foreach (var attribute in source.Attributes)
        {
            if (!string.Equals(attribute.Name, GeometryAttributeNames.Color, StringComparison.OrdinalIgnoreCase))
                _shared.Add(attribute);
        }

        _output.Attributes.Clear();
        foreach (var attribute in _shared)
        {
            _output.Attributes.Add(attribute);
        }

        _output.Attributes.Add(output);
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private void BuildFaceToPart(MeshGeometry geometry)
    {
        if (_faceToPart.Length != geometry.FaceCount)
            _faceToPart = new int[geometry.FaceCount];

        // No parts means one implicit part covering everything
        Array.Fill(_faceToPart, geometry.Parts.Length == 0 ? 0 : -1);
        for (var partIndex = 0; partIndex < geometry.Parts.Length; partIndex++)
        {
            var part = geometry.Parts[partIndex];
            var end = Math.Min(part.FaceStart + part.FaceCount, geometry.FaceCount);
            for (var faceIndex = part.FaceStart; faceIndex < end; faceIndex++)
            {
                _faceToPart[faceIndex] = partIndex;
            }
        }
    }

    private enum Sources
    {
        PartIndex,
        PartSeedIndex,
        FaceAttribute,
    }

    private enum WrapModes
    {
        Repeat,
        Clamp,
    }

    private readonly MeshGeometry _output = new();
    private readonly List<GeometryAttribute> _shared = [];
    private int[] _faceToPart = [];

    [Input(Guid = "9d3b6e58-1c47-4fa2-b8e0-73a5c9d2f614")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "e2a95c07-6b38-4d1f-a4c6-58f0b7e3d921")]
    public readonly InputSlot<List<Vector4>> Colors = new();

    [Input(Guid = "4b7f1d83-a952-4c60-9e2d-c1a8f6b5e037", MappedType = typeof(Sources))]
    public readonly InputSlot<int> Source = new();

    [Input(Guid = "a6c48e21-3d9f-4b75-8f0a-2e7d1c9b5348")]
    public readonly InputSlot<string> AttributeName = new();

    [Input(Guid = "17e0d5a9-b4c2-4f83-96b7-d3f8a1e6c452", MappedType = typeof(WrapModes))]
    public readonly InputSlot<int> Wrap = new();

    [Input(Guid = "f5b29a64-08d7-4e1c-a3f9-6c0b4d8e2a17")]
    public readonly InputSlot<bool> OnlySelected = new();
}
