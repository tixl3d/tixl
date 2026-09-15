namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Gradient through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("1a1605fe-b256-4806-a3e1-210496350ea0")]
public sealed class RerouteGradient : Instance<RerouteGradient>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "d28c30fc-8f1d-48e3-ba59-9aa6f28ac428")]
    public readonly Slot<T3.Core.DataTypes.Gradient> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "bcd622ce-1021-465a-bef6-a4b7d10003d2")]
    public readonly InputSlot<T3.Core.DataTypes.Gradient> Input = new();
}
