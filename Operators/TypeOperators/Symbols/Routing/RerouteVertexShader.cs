namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.VertexShader through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("4eaba812-9652-4cae-b761-96cf714961b6")]
public sealed class RerouteVertexShader : Instance<RerouteVertexShader>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "ac2cad49-8023-408d-8538-aed7df2ff7d4")]
    public readonly Slot<T3.Core.DataTypes.VertexShader> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "c3a0c9e2-e08e-4fe0-9a45-bd1825976736")]
    public readonly InputSlot<T3.Core.DataTypes.VertexShader> Input = new();
}
