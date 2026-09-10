namespace Types.Routing;

[Guid("edeb4c56-ca52-4f54-88df-2cf57bd82095")]
public sealed class RerouteCpuAccessFlags : Instance<RerouteCpuAccessFlags>, IRerouteNode
{
    [Output(Guid = "78f15cc0-754b-4aa1-93c6-388c675f4b0c")]
    public readonly Slot<SharpDX.Direct3D11.CpuAccessFlags> Output = new();

    [Input(Guid = "e78d311f-8d0e-425d-89f7-be12cc09a26e")]
    public readonly InputSlot<SharpDX.Direct3D11.CpuAccessFlags> Input = new();
}
