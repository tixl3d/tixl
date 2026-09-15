namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.CurveGeometry through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("228a7275-1371-4819-9195-ae035d2c63a2")]
public sealed class RerouteCurveGeometry : Instance<RerouteCurveGeometry>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "a188407e-420f-4df3-97a6-24c2b91adb78")]
    public readonly Slot<T3.Core.DataTypes.CurveGeometry> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "856fb959-d337-4f66-bf71-6ee3ddec0fe0")]
    public readonly InputSlot<T3.Core.DataTypes.CurveGeometry> Input = new();
}
