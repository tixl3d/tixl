namespace Types.Routing;

// Passes SharpDX.Direct3D11.ResourceOptionFlags through the .t3 connection; no C# evaluator is needed.
[Guid("c6bcc79b-fd52-40b6-9b0b-e3c43eb88c22")]
public sealed class RerouteResourceOptionFlags : Instance<RerouteResourceOptionFlags>, IRerouteNode
{
    [Output(Guid = "ce4055a2-0c9c-4e8e-9244-894d8194bea3")]
    public readonly Slot<SharpDX.Direct3D11.ResourceOptionFlags> Output = new();

    [Input(Guid = "b4a1a907-2c80-4de5-a6dc-b139050e7ae4")]
    public readonly InputSlot<SharpDX.Direct3D11.ResourceOptionFlags> Input = new();
}
