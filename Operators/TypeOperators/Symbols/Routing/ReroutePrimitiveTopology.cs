namespace Types.Routing;

/// <summary>Passes SharpDX.Direct3D.PrimitiveTopology through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("0f98aa11-4e24-4546-8aa4-ab7621c084bf")]
public sealed class ReroutePrimitiveTopology : Instance<ReroutePrimitiveTopology>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "5ee1f523-cd50-4eb7-9130-ea687526cd5a")]
    public readonly Slot<SharpDX.Direct3D.PrimitiveTopology> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "9a7a2f09-02a3-4917-be03-107f6fda9558")]
    public readonly InputSlot<SharpDX.Direct3D.PrimitiveTopology> Input = new();
}
