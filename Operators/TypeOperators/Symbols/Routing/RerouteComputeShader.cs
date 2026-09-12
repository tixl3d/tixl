namespace Types.Routing;

// Passes T3.Core.DataTypes.ComputeShader through the .t3 connection; no C# evaluator is needed.
[Guid("cb80cc64-6ab1-4930-bfbb-f28f44385d40")]
public sealed class RerouteComputeShader : Instance<RerouteComputeShader>, IRerouteNode
{
    [Output(Guid = "fa59c0fe-2602-4512-a39c-a3d222783931")]
    public readonly Slot<T3.Core.DataTypes.ComputeShader> Output = new();

    [Input(Guid = "5788e777-2e12-4bc2-88ed-384a716b83ec")]
    public readonly InputSlot<T3.Core.DataTypes.ComputeShader> Input = new();
}
