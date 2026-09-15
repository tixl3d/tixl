namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.StructuredList through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("65f98fa4-5e8b-45ed-804f-5843cf331817")]
public sealed class RerouteStructuredList : Instance<RerouteStructuredList>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "7250b11a-24c4-45c9-a6e0-803064b3bb42")]
    public readonly Slot<T3.Core.DataTypes.StructuredList> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "1b122f74-cecd-4aa6-9eaa-f3c0b8d49954")]
    public readonly InputSlot<T3.Core.DataTypes.StructuredList> Input = new();
}
