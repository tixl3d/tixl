namespace Types.Routing;

[Guid("7df77232-14f0-4a13-849b-a7b5d2315dca")]
public sealed class RerouteVectorField : Instance<RerouteVectorField>, IRerouteNode
{
    [Output(Guid = "7f3a6bcc-bcf7-45bd-835e-d7868c2a54be")]
    public readonly Slot<T3.Core.DataTypes.VectorField> Output = new();

    [Input(Guid = "ce71b474-58eb-4b1b-b9fb-8a86c9ba7a22")]
    public readonly InputSlot<T3.Core.DataTypes.VectorField> Input = new();
}
