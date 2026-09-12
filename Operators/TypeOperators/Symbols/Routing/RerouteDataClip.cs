namespace Types.Routing;

// Passes T3.Core.DataTypes.DataSet.DataClip through the .t3 connection; no C# evaluator is needed.
[Guid("94233005-3770-455a-85c0-7334bc3611b0")]
public sealed class RerouteDataClip : Instance<RerouteDataClip>, IRerouteNode
{
    [Output(Guid = "586d8a33-5ab5-4f34-ac89-054022eedac9")]
    public readonly Slot<T3.Core.DataTypes.DataSet.DataClip> Output = new();

    [Input(Guid = "a7dd374f-d65a-4416-8fbe-dae4e16dafc0")]
    public readonly InputSlot<T3.Core.DataTypes.DataSet.DataClip> Input = new();
}
