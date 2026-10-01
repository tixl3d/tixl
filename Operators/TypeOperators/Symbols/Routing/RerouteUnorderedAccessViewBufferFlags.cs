namespace Types.Routing;

[Guid("199e4bc5-d36e-4fad-b947-8461ebdf32ad")]
public sealed class RerouteUnorderedAccessViewBufferFlags : Instance<RerouteUnorderedAccessViewBufferFlags>, IRerouteNode
{
    [Output(Guid = "015e59d3-3471-4733-8411-dc82128a2696")]
    public readonly Slot<SharpDX.Direct3D11.UnorderedAccessViewBufferFlags> Output = new();

    [Input(Guid = "14ad95be-2b46-4b5b-9240-f89fcb25be72")]
    public readonly InputSlot<SharpDX.Direct3D11.UnorderedAccessViewBufferFlags> Input = new();
}
