namespace Types.Routing;

[Guid("fe434db7-99dd-4407-881c-8d1c624c3fd3")]
public sealed class RerouteInt3 : Instance<RerouteInt3>, IRerouteNode
{
    [Output(Guid = "e380ad99-ee5b-4f60-bc16-c9e2d9362b22")]
    public readonly Slot<T3.Core.DataTypes.Vector.Int3> Output = new();

    [Input(Guid = "8d35ba89-b9a4-4942-95f7-6002800c1373")]
    public readonly InputSlot<T3.Core.DataTypes.Vector.Int3> Input = new();
}
