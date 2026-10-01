namespace Types.Routing;

[Guid("98932409-f9e8-46ca-bc4f-ccc08e021729")]
public sealed class RerouteBindFlags : Instance<RerouteBindFlags>, IRerouteNode
{
    [Output(Guid = "084a99fa-97d6-4206-8b45-14cfaa81e023")]
    public readonly Slot<SharpDX.Direct3D11.BindFlags> Output = new();

    [Input(Guid = "3fd608d0-33f7-4410-875e-2e4d672e87fb")]
    public readonly InputSlot<SharpDX.Direct3D11.BindFlags> Input = new();
}
