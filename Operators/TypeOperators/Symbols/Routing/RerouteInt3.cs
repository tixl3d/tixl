namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Vector.Int3 through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("fe434db7-99dd-4407-881c-8d1c624c3fd3")]
public sealed class RerouteInt3 : Instance<RerouteInt3>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "e380ad99-ee5b-4f60-bc16-c9e2d9362b22")]
    public readonly Slot<T3.Core.DataTypes.Vector.Int3> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "8d35ba89-b9a4-4942-95f7-6002800c1373")]
    public readonly InputSlot<T3.Core.DataTypes.Vector.Int3> Input = new();
}
