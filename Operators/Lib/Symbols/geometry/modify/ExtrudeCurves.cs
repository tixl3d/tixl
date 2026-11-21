#nullable enable
using System;
using System.Collections.Generic;
using LibTessDotNet;
using Lib.Utils;

namespace Lib.geometry.modify;

/// <summary>
/// Fills the closed curve primitives of a Geometry with polygons and optionally extrudes them: front cap, back cap
/// and side walls with a rounded or chamfered bevel, in one op because the bevel needs the cap boundary and the walls
/// to agree. One output part per input part (glyph), with the input's part attributes, so per-character selection and
/// coloring keep working. With KeepCurves the source curves stay in each part, after its polygons. Polygons in the
/// input are not passed through.
/// </summary>
[Guid("b6e3f8d1-4a27-4c95-9d0e-7f2c5a1b8e63")]
[ExportDependencies("LibTessDotNet.dll")]
internal sealed class ExtrudeCurves : Instance<ExtrudeCurves>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "1c9a5e7d-3b62-4f08-a4d1-8e6b0c2f7a95")]
    public readonly Slot<Geometry?> Result = new();

    public ExtrudeCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var curves = Curves.GetValue(context);
        var depth = MathF.Max(Depth.GetValue(context), 0);
        var bevel = MathF.Max(Bevel.GetValue(context), 0);
        var bevelSegments = Math.Clamp(BevelSegments.GetValue(context), 1, 16);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-5f);
        var front = Front.GetValue(context);
        var back = Back.GetValue(context);
        var sides = Sides.GetValue(context);
        var windingRule = EvenOdd.GetValue(context) ? WindingRule.EvenOdd : WindingRule.NonZero;
        var weight = Weight.GetValue(context);
        var flatShading = FlatShading.GetValue(context);
        var smoothAngle = Math.Clamp(SmoothAngle.GetValue(context), 0f, 180f);
        var keepCurves = KeepCurves.GetValue(context);
        var depthAttribute = DepthAttribute.GetValue(context);
        _statusMessage = string.Empty;
        _lastSource = curves;

        if (curves == null || curves.PrimitiveCount == 0)
        {
            Result.Value = null;
            return;
        }

        _builder.Begin();

        var parts = curves.Parts;
        var hasDepthFactors = CollectDepthFactors(curves, depthAttribute);
        var curveViews = curves.Curves;
        var partCount = parts.Length > 0 ? parts.Length : 1;
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            var primitiveStart = parts.Length > 0 ? parts[partIndex].PrimitiveStart : 0;
            var primitiveEnd = parts.Length > 0 ? primitiveStart + parts[partIndex].PrimitiveCount : curves.PrimitiveCount;
            var pivot = parts.Length > 0 ? parts[partIndex].Pivot : Vector3.Zero;
            var id = parts.Length > 0 ? parts[partIndex].Id : 0;
            var seed = parts.Length > 0 ? parts[partIndex].SeedIndex : 0;
            var partDepth = hasDepthFactors ? MathF.Max(depth * _depthFactors[partIndex], 0) : depth;

            // A bevel that doesn't fit into the depth is halved from both ends
            var partBevel = partDepth > 0 ? MathF.Min(bevel, partDepth * 0.5f) : 0;
            _builder.BuildPart(curves, curveViews, primitiveStart, primitiveEnd, tolerance, partDepth, partBevel, bevelSegments, weight, flatShading,
                               smoothAngle, front, back, sides, windingRule, keepCurves, pivot, id, seed);
        }

        _builder.Finish(_output, curves);
        Result.Value = _output;
    }

    /// <summary>
    /// Depth factor per part from the named attribute: a part attribute directly, others through their points, taking
    /// the part's largest value. Without parts the whole geometry is one part.
    /// </summary>
    /// <returns>False when every part uses Depth as it is.</returns>
    private bool CollectDepthFactors(Geometry curves, string? attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return false;

        var partCount = Math.Max(curves.Parts.Length, 1);
        if (_depthFactors.Length < partCount)
            _depthFactors = new float[partCount];

        if (_pointWeights.Length < curves.PointCount)
            _pointWeights = new float[curves.PointCount];

        bool found;
        if (curves.Parts.Length > 0)
        {
            found = GeometryElements.TryGetWeights(curves, AttributeDomain.Part, attributeName, _depthFactors, _pointWeights);
        }
        else
        {
            found = curves.TryGetPointWeights(attributeName, _pointWeights);
            var largest = 0f;
            for (var point = 0; point < curves.PointCount; point++)
                largest = MathF.Max(largest, _pointWeights[point]);

            _depthFactors[0] = largest;
        }

        if (found)
            return true;

        _statusMessage = $"No float attribute '{attributeName}'; every part uses Depth.";
        return false;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    #region ICustomDropdownHolder - float attributes of the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => DepthAttribute.Value;

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => GeometryAttributeChoices.GetAttributeNames(_lastSource, null, floatsOnly: true);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId == DepthAttribute.Input.InputDefinition.Id && selected != null)
            DepthAttribute.SetTypedInputValue(selected);
    }
    #endregion

    private float[] _depthFactors = [];
    private float[] _pointWeights = [];
    private Geometry? _lastSource;
    private string _statusMessage = string.Empty;
    private readonly CurveSolidBuilder _builder = new();
    private readonly Geometry _output = new();

    [Input(Guid = "4a7e2c9f-6d15-4b83-a0e6-9c3f8b1d5e27")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "c1f5b8e3-2a94-4d67-8e0b-7d3a9f6c2b14")]
    public readonly InputSlot<float> Depth = new();

    [Input(Guid = "9e3a7d1c-5b48-4f26-b9c0-1a6e4d8f3c72")]
    public readonly InputSlot<float> Bevel = new();

    [Input(Guid = "2b8d5f7a-e319-4c60-a5d8-6f0c3e9a1b48")]
    public readonly InputSlot<int> BevelSegments = new();

    [Input(Guid = "5c0e9b3d-7a41-4f28-b6d3-2e8f1a4c7d95")]
    public readonly InputSlot<float> Weight = new();

    [Input(Guid = "8d4b2e6f-1c93-4a57-9f0e-6b3d5c8a2e41")]
    public readonly InputSlot<bool> FlatShading = new();

    [Input(Guid = "1f7a3c5e-9b62-4d18-a4c7-3e0f8b6d2a59")]
    public readonly InputSlot<float> SmoothAngle = new();

    [Input(Guid = "7d4c1a9e-8f52-4b37-9e6a-3c1b5d7f2a80")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "e5b2d8c4-1a76-4f93-b0d5-8c4e7a2f9b16")]
    public readonly InputSlot<bool> Front = new();

    [Input(Guid = "3f9c6e2b-d485-4a10-8b7e-5d2a1c8f4e63")]
    public readonly InputSlot<bool> Back = new();

    [Input(Guid = "a8e1c4f7-2b93-4d58-9a6c-0e5d3b7f1c29")]
    public readonly InputSlot<bool> Sides = new();

    [Input(Guid = "6c2f9b5e-7d31-4a84-b1e0-4f8c2a6d9e75")]
    public readonly InputSlot<bool> EvenOdd = new();

    [Input(Guid = "532a6559-788f-44f4-ba03-7ce2b3e7561d")]
    public readonly InputSlot<bool> KeepCurves = new();

    [Input(Guid = "5c7feaba-714e-4134-bc65-e8438ef6839d")]
    public readonly InputSlot<string> DepthAttribute = new();
}
