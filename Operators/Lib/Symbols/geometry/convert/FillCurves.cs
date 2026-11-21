#nullable enable
using Lib.Utils;
using LibTessDotNet;

namespace Lib.geometry.convert;

/// <summary>
/// Fills the closed curves of a Geometry with flat polygons, holes included, so they can be modelled further or
/// rendered; the flat counterpart of [ExtrudeCurves] (Houdini's PolyFill). One output part per input part, with the
/// input's part attributes. Polygons in the input are not passed through.
/// </summary>
[Guid("daab8738-babd-4036-8581-22e3dc55e4ba")]
[ExportDependencies("LibTessDotNet.dll")]
internal sealed class FillCurves : Instance<FillCurves>, IStatusProvider
{
    [Output(Guid = "4dfc042d-c7fa-4ce1-b774-2892a67f1d6c")]
    public readonly Slot<Geometry?> Result = new();

    public FillCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var curves = Curves.GetValue(context);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-5f);
        var weight = Weight.GetValue(context);
        var windingRule = EvenOdd.GetValue(context) ? WindingRule.EvenOdd : WindingRule.NonZero;
        var keepCurves = KeepCurves.GetValue(context);
        if (curves == null || curves.PrimitiveCount == 0)
        {
            Result.Value = null;
            return;
        }

        _builder.Lattice = MathF.Max(Lattice.GetValue(context), 0);
        _builder.FillFlat(curves, tolerance, weight, windingRule, keepCurves, _output);
        _statusMessage = _builder.LatticeIssue ?? string.Empty;
        Result.Value = _output;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private string _statusMessage = string.Empty;
    private readonly CurveSolidBuilder _builder = new();
    private readonly Geometry _output = new();

    [Input(Guid = "2b970621-dc0a-41d8-9220-3dd19a707af1")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "d1deb9b8-6ffd-476b-b9b4-fbd32845b0f0")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "ac6b705b-b962-4f92-8e4a-451d787f57bb")]
    public readonly InputSlot<float> Weight = new();

    [Input(Guid = "02b5e3a3-064e-4e1b-85b9-1d3ca685958d")]
    public readonly InputSlot<bool> EvenOdd = new();

    [Input(Guid = "a2bca4ff-9cd9-4f97-9fbf-a0ee9351b86e")]
    public readonly InputSlot<bool> KeepCurves = new();

    [Input(Guid = "eff0d217-6548-4871-aa00-31c85029e69c")]
    public readonly InputSlot<float> Lattice = new();
}
