namespace Types.Routing;

[Guid("fff832dd-d776-4789-9e0f-41ca344c80e5")]
public sealed class RerouteRasterizerState : Instance<RerouteRasterizerState>, IRerouteNode
{
    [Output(Guid = "aa7f0e7c-618e-4d2a-91e9-7a502c3ee201")]
    public readonly Slot<SharpDX.Direct3D11.RasterizerState> Output = new();

    [Input(Guid = "8e511ae7-fb10-4af1-bc75-343b3a509e76")]
    public readonly InputSlot<SharpDX.Direct3D11.RasterizerState> Input = new();
}
