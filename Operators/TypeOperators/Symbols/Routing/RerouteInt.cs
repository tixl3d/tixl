namespace Types.Routing;

/// <summary>Passes int through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("aced3297-133d-479d-833b-736f76412340")]
public sealed class RerouteInt : Instance<RerouteInt>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "5538bf75-20a9-452d-9713-1ee51993ce6d")]
    public readonly Slot<int> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "25d025fc-5a92-4439-b2eb-6657d00a6676")]
    public readonly InputSlot<int> Input = new();
}
