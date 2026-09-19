namespace Types.Routing;

[Guid("09d3027c-7387-4ded-9691-3f55744eda41")]
public sealed class RerouteBlendState : Instance<RerouteBlendState>, IRerouteNode
{
    [Output(Guid = "c09df239-d2d6-4754-97d8-684a80a7e869")]
    public readonly Slot<SharpDX.Direct3D11.BlendState> Output = new();

    [Input(Guid = "91a637bd-b2d6-4868-a926-46e092a06502")]
    public readonly InputSlot<SharpDX.Direct3D11.BlendState> Input = new();
}
