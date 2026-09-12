namespace Types.Routing;

// Passes SharpDX.Direct3D11.ResourceUsage through the .t3 connection; no C# evaluator is needed.
[Guid("1faf7577-3416-4408-b0f8-656a9e8c02dc")]
public sealed class RerouteResourceUsage : Instance<RerouteResourceUsage>, IRerouteNode
{
    [Output(Guid = "ce8c47e0-959e-43ef-a9a7-e4a5ada6b19e")]
    public readonly Slot<SharpDX.Direct3D11.ResourceUsage> Output = new();

    [Input(Guid = "2b8fca86-2436-410e-94f0-2bade90a7112")]
    public readonly InputSlot<SharpDX.Direct3D11.ResourceUsage> Input = new();
}
