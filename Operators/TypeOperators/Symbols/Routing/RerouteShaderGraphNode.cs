namespace Types.Routing;

// Passes T3.Core.DataTypes.ShaderGraphNode through the .t3 connection; no C# evaluator is needed.
[Guid("f46cd7e5-291c-4dfa-9eb0-ecc97ee317b8")]
public sealed class RerouteShaderGraphNode : Instance<RerouteShaderGraphNode>, IRerouteNode
{
    [Output(Guid = "1747d94c-9dff-4002-9986-4b111a6ff374")]
    public readonly Slot<T3.Core.DataTypes.ShaderGraphNode> Output = new();

    [Input(Guid = "52e4901e-60c3-410d-b895-9eb4faecfc0e")]
    public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Input = new();
}
