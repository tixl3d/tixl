namespace Types.Routing;

// Passes SharpDX.Direct3D11.ColorWriteMaskFlags through the .t3 connection; no C# evaluator is needed.
[Guid("147e1ea2-cf9b-4111-81f1-5229a345d7ca")]
public sealed class RerouteColorWriteMaskFlags : Instance<RerouteColorWriteMaskFlags>, IRerouteNode
{
    [Output(Guid = "fa166c4c-4013-4ae4-b82f-33f38744d07f")]
    public readonly Slot<SharpDX.Direct3D11.ColorWriteMaskFlags> Output = new();

    [Input(Guid = "72edd0a5-7f6a-4cd7-80eb-5a7708e3fadc")]
    public readonly InputSlot<SharpDX.Direct3D11.ColorWriteMaskFlags> Input = new();
}
