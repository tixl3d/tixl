namespace Types.Routing;

[Guid("b6c3ddb4-f103-4ad6-83fb-bd6f4438d62a")]
public sealed class RerouteVector4List : Instance<RerouteVector4List>, IRerouteNode
{
    [Output(Guid = "41141c56-ffa0-4cde-8148-9b2babfd787d")]
    public readonly Slot<System.Collections.Generic.List<System.Numerics.Vector4>> Output = new();

    [Input(Guid = "c3374206-2c91-4255-bdd2-4fe197867075")]
    public readonly InputSlot<System.Collections.Generic.List<System.Numerics.Vector4>> Input = new();
}
