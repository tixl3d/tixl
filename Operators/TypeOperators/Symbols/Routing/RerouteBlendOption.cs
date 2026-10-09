namespace Types.Routing;

[Guid("bc46a1a1-5832-4f5a-a7cc-99a71d43d6cc")]
public sealed class RerouteBlendOption : Instance<RerouteBlendOption>, IRerouteNode
{
    [Output(Guid = "ac962881-50d8-419c-83c2-19d3e1cdffb7")]
    public readonly Slot<SharpDX.Direct3D11.BlendOption> Output = new();

    [Input(Guid = "c6206c27-0c2f-458e-a6e2-bcd87d9a8705")]
    public readonly InputSlot<SharpDX.Direct3D11.BlendOption> Input = new();
}
