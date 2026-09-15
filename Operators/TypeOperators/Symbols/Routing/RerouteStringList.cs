namespace Types.Routing;

/// <summary>Passes System.Collections.Generic.List&lt;string&gt; through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("1a256d0e-f56d-4f13-b2a1-2147ad63248d")]
public sealed class RerouteStringList : Instance<RerouteStringList>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "cb3f61de-fdc1-4c6c-99c6-5abf15e0dc3d")]
    public readonly Slot<System.Collections.Generic.List<string>> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "c488482f-b611-4dd3-b608-3bf7d054fa1e")]
    public readonly InputSlot<System.Collections.Generic.List<string>> Input = new();
}
