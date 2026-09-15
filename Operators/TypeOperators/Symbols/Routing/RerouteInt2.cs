namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Vector.Int2 through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("abd018dc-c414-4897-a9b3-eb2375da196d")]
public sealed class RerouteInt2 : Instance<RerouteInt2>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "cbf34915-6dc9-442d-b5f5-69adf14e163e")]
    public readonly Slot<T3.Core.DataTypes.Vector.Int2> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "db79a1fe-095a-4cc8-9d27-ee16889ba398")]
    public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Input = new();
}
