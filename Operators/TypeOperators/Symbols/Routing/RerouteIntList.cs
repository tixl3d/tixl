namespace Types.Routing;

/// <summary>Passes System.Collections.Generic.List&lt;int&gt; through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("df8d3f09-86e3-457e-943f-0cf968daf1a8")]
public sealed class RerouteIntList : Instance<RerouteIntList>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "67a0dd3a-2386-47d6-844d-eb377a2704cc")]
    public readonly Slot<System.Collections.Generic.List<int>> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "e1f6dc9b-6f33-4520-990f-1dbca7c63b8f")]
    public readonly InputSlot<System.Collections.Generic.List<int>> Input = new();
}
