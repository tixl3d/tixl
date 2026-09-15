namespace Types.Routing;

/// <summary>Passes SharpDX.Direct3D11.Filter through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("38f55b51-360a-44df-b51a-cb9e013fc9a7")]
public sealed class RerouteFilter : Instance<RerouteFilter>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "681804b9-b826-4a22-ac0e-a8a0bc114e8b")]
    public readonly Slot<SharpDX.Direct3D11.Filter> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "fe3fa1a7-d5ba-4473-9a2f-fa3f789fc6b7")]
    public readonly InputSlot<SharpDX.Direct3D11.Filter> Input = new();
}
