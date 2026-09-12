namespace Types.Routing;

// Passes SharpDX.Direct3D11.CullMode through the .t3 connection; no C# evaluator is needed.
[Guid("ca0062a7-1add-4a90-8934-26de96897d6e")]
public sealed class RerouteCullMode : Instance<RerouteCullMode>, IRerouteNode
{
    [Output(Guid = "6a964ae8-842d-41b0-b634-38ad05b1776d")]
    public readonly Slot<SharpDX.Direct3D11.CullMode> Output = new();

    [Input(Guid = "23544843-643c-4d9b-bf79-60ef98c69fb1")]
    public readonly InputSlot<SharpDX.Direct3D11.CullMode> Input = new();
}
