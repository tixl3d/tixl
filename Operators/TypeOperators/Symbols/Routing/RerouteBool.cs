namespace Types.Routing;

/// <summary>Passes bool through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("0334db8e-e2d4-46d2-b2be-c48d2dc7c1e5")]
public sealed class RerouteBool : Instance<RerouteBool>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "a525f4b8-508a-4a8f-b3ea-fd00f991c971")]
    public readonly Slot<bool> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "b3528ff7-b1c2-4917-ae6f-435d298acd99")]
    public readonly InputSlot<bool> Input = new();
}
