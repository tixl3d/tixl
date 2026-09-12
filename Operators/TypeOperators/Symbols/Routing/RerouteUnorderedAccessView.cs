namespace Types.Routing;

// Passes SharpDX.Direct3D11.UnorderedAccessView through the .t3 connection; no C# evaluator is needed.
[Guid("7aef6927-a8d4-43fc-8353-67b7f20c62b9")]
public sealed class RerouteUnorderedAccessView : Instance<RerouteUnorderedAccessView>, IRerouteNode
{
    [Output(Guid = "8c78682d-d3ba-40e0-b791-95bc08a4b151")]
    public readonly Slot<SharpDX.Direct3D11.UnorderedAccessView> Output = new();

    [Input(Guid = "6a3ee5da-64a0-4ed4-abe2-5508d95bed3d")]
    public readonly InputSlot<SharpDX.Direct3D11.UnorderedAccessView> Input = new();
}
