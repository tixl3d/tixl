namespace Types.Routing;

// Passes System.Collections.Generic.List<int> through the .t3 connection; no C# evaluator is needed.
[Guid("df8d3f09-86e3-457e-943f-0cf968daf1a8")]
public sealed class RerouteIntList : Instance<RerouteIntList>, IRerouteNode
{
    [Output(Guid = "67a0dd3a-2386-47d6-844d-eb377a2704cc")]
    public readonly Slot<System.Collections.Generic.List<int>> Output = new();

    [Input(Guid = "e1f6dc9b-6f33-4520-990f-1dbca7c63b8f")]
    public readonly InputSlot<System.Collections.Generic.List<int>> Input = new();
}
