namespace Types.Routing;

[Guid("fbc12c1a-ec4f-46a5-91c3-31ddd923bf5f")]
public sealed class RerouteRenderTargetReference : Instance<RerouteRenderTargetReference>, IRerouteNode
{
    [Output(Guid = "f07e866b-ce6d-4342-9c90-d1ac16cdf135")]
    public readonly Slot<T3.Core.DataTypes.RenderTargetReference> Output = new();

    [Input(Guid = "d2fff25e-1ad4-4c45-a6da-08692a7cef10")]
    public readonly InputSlot<T3.Core.DataTypes.RenderTargetReference> Input = new();
}
