namespace Types.Routing;

[Guid("abd018dc-c414-4897-a9b3-eb2375da196d")]
public sealed class RerouteInt2 : Instance<RerouteInt2>, IRerouteNode
{
    [Output(Guid = "cbf34915-6dc9-442d-b5f5-69adf14e163e")]
    public readonly Slot<T3.Core.DataTypes.Vector.Int2> Output = new();

    [Input(Guid = "db79a1fe-095a-4cc8-9d27-ee16889ba398")]
    public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Input = new();
}
