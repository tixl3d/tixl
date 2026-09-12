namespace Types.Routing;

// Passes T3.Core.DataTypes.Curve through the .t3 connection; no C# evaluator is needed.
[Guid("04df325b-e082-497f-952d-b2c2fb394516")]
public sealed class RerouteCurve : Instance<RerouteCurve>, IRerouteNode
{
    [Output(Guid = "a3eaddba-e98d-4ce9-b5b6-4fbecdd9367f")]
    public readonly Slot<T3.Core.DataTypes.Curve> Output = new();

    [Input(Guid = "79d9c357-3269-4cee-8160-a19bdeefa48e")]
    public readonly InputSlot<T3.Core.DataTypes.Curve> Input = new();
}
