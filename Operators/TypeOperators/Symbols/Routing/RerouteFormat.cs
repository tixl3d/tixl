namespace Types.Routing;

/// <summary>Passes SharpDX.DXGI.Format through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("b5c8085e-05dc-4e58-92a5-44aa1f0e7abb")]
public sealed class RerouteFormat : Instance<RerouteFormat>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "918e3112-d2b4-4afc-890d-3c03481638de")]
    public readonly Slot<SharpDX.DXGI.Format> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "58a6d7e2-1968-4a4f-98f3-4f9c5fa2fedf")]
    public readonly InputSlot<SharpDX.DXGI.Format> Input = new();
}
