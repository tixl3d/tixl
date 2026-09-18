namespace Types.Routing;

[Guid("68d70eca-47a5-4ec1-99aa-70b9ae2e7728")]
public sealed class RerouteBuffer : Instance<RerouteBuffer>, IRerouteNode
{
    [Output(Guid = "3a20946c-18e5-4560-ac6d-5a1d91690872")]
    public readonly Slot<SharpDX.Direct3D11.Buffer> Output = new();

    [Input(Guid = "0d36e454-5794-4394-b506-38bfe2ad2187")]
    public readonly InputSlot<SharpDX.Direct3D11.Buffer> Input = new();
}
