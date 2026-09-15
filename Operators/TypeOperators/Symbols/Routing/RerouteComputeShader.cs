namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.ComputeShader through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("cb80cc64-6ab1-4930-bfbb-f28f44385d40")]
public sealed class RerouteComputeShader : Instance<RerouteComputeShader>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "fa59c0fe-2602-4512-a39c-a3d222783931")]
    public readonly Slot<T3.Core.DataTypes.ComputeShader> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "5788e777-2e12-4bc2-88ed-384a716b83ec")]
    public readonly InputSlot<T3.Core.DataTypes.ComputeShader> Input = new();
}
