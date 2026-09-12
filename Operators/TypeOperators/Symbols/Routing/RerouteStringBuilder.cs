namespace Types.Routing;

// Passes System.Text.StringBuilder through the .t3 connection; no C# evaluator is needed.
[Guid("d88ac991-b40f-44a1-a120-12a8bf94125a")]
public sealed class RerouteStringBuilder : Instance<RerouteStringBuilder>, IRerouteNode
{
    [Output(Guid = "e3e0eae6-b82d-4b14-9bf8-4d6119c13357")]
    public readonly Slot<System.Text.StringBuilder> Output = new();

    [Input(Guid = "662d71cf-d4cf-4eab-b495-74c71aa26d50")]
    public readonly InputSlot<System.Text.StringBuilder> Input = new();
}
