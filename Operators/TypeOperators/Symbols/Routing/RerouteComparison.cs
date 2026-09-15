namespace Types.Routing;

/// <summary>Passes SharpDX.Direct3D11.Comparison through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("751aacca-1381-4582-ad22-aac1815d81e9")]
public sealed class RerouteComparison : Instance<RerouteComparison>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "dea7b435-7dc6-429c-9c6f-85a4a217235d")]
    public readonly Slot<SharpDX.Direct3D11.Comparison> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "e7304091-6489-4d33-9442-9f62543c4e1d")]
    public readonly InputSlot<SharpDX.Direct3D11.Comparison> Input = new();
}
