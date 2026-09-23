namespace Types.Routing;

[Guid("751aacca-1381-4582-ad22-aac1815d81e9")]
public sealed class RerouteComparison : Instance<RerouteComparison>, IRerouteNode
{
    [Output(Guid = "dea7b435-7dc6-429c-9c6f-85a4a217235d")]
    public readonly Slot<SharpDX.Direct3D11.Comparison> Output = new();

    [Input(Guid = "e7304091-6489-4d33-9442-9f62543c4e1d")]
    public readonly InputSlot<SharpDX.Direct3D11.Comparison> Input = new();
}
