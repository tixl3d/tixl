namespace Types.Routing;

/// <summary>Passes System.Numerics.Vector4[] through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("64e71fcf-b3c2-4428-aaeb-6dcf34d5be6a")]
public sealed class RerouteVector4Array : Instance<RerouteVector4Array>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "cb0e0d94-8c3b-44a3-b402-12fb5658529a")]
    public readonly Slot<System.Numerics.Vector4[]> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "3dbf4eaa-4da4-4f85-9c5e-320b2583bbba")]
    public readonly InputSlot<System.Numerics.Vector4[]> Input = new();
}
