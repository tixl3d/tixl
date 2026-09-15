namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Dict&lt;float&gt; through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("8d48de87-c55c-4224-9bbe-5b94d416dee4")]
public sealed class RerouteFloatDict : Instance<RerouteFloatDict>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "789b1ec2-5c83-468f-bd5f-6fb2ed1f173f")]
    public readonly Slot<T3.Core.DataTypes.Dict<float>> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "9b37212a-c061-47aa-bfd8-b41b9ed6c45a")]
    public readonly InputSlot<T3.Core.DataTypes.Dict<float>> Input = new();
}
