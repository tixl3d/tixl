namespace Types.Routing;

[Guid("6f778f18-aadc-424e-b2c7-fc6190b8c980")]
public sealed class RerouteDepthStencilView : Instance<RerouteDepthStencilView>, IRerouteNode
{
    [Output(Guid = "2da76772-f226-4178-9a25-bcf04c93e731")]
    public readonly Slot<SharpDX.Direct3D11.DepthStencilView> Output = new();

    [Input(Guid = "2b80030e-aa35-487e-8306-f969ab0674c0")]
    public readonly InputSlot<SharpDX.Direct3D11.DepthStencilView> Input = new();
}
