namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.VectorField through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("7df77232-14f0-4a13-849b-a7b5d2315dca")]
public sealed class RerouteVectorField : Instance<RerouteVectorField>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "7f3a6bcc-bcf7-45bd-835e-d7868c2a54be")]
    public readonly Slot<T3.Core.DataTypes.VectorField> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "ce71b474-58eb-4b1b-b9fb-8a86c9ba7a22")]
    public readonly InputSlot<T3.Core.DataTypes.VectorField> Input = new();
}
