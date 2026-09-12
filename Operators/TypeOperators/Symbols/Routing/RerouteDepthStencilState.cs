namespace Types.Routing;

// Passes SharpDX.Direct3D11.DepthStencilState through the .t3 connection; no C# evaluator is needed.
[Guid("dc205ab1-8e42-43eb-ba9a-9ff780aadc62")]
public sealed class RerouteDepthStencilState : Instance<RerouteDepthStencilState>, IRerouteNode
{
    [Output(Guid = "9537a895-d63a-420f-b1df-9331525ad5f2")]
    public readonly Slot<SharpDX.Direct3D11.DepthStencilState> Output = new();

    [Input(Guid = "b739f1d2-2ba0-4e98-a62d-379ff8f70683")]
    public readonly InputSlot<SharpDX.Direct3D11.DepthStencilState> Input = new();
}
