namespace Types.Routing;

/// <summary>Passes string through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("b6c47dc1-e755-4221-a4ec-65440c631479")]
public sealed class RerouteString : Instance<RerouteString>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "ccfcc636-f1cd-4681-93f7-37ca464bbdbb")]
    public readonly Slot<string> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "30a16a09-c7ae-4e30-b21e-f94a3e85d534")]
    public readonly InputSlot<string> Input = new();
}
