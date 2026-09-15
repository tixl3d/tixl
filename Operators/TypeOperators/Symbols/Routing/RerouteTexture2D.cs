namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Texture2D through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("e32a4006-d2d1-4ab8-baa9-0970f3683af2")]
public sealed class RerouteTexture2D : Instance<RerouteTexture2D>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "3bec74ca-b894-429c-ba5b-fa6d5eeaee96")]
    public readonly Slot<T3.Core.DataTypes.Texture2D> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "18ddd0a1-19a0-4891-b2a8-443dd550cab2")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Input = new();
}
