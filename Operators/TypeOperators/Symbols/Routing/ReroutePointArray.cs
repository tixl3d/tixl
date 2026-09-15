namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.Point[] through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("aabce273-2a21-4e34-8b1e-9a9720944aa7")]
public sealed class ReroutePointArray : Instance<ReroutePointArray>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "8ff5a597-d0d7-4e7c-a286-1642611c06cd")]
    public readonly Slot<T3.Core.DataTypes.Point[]> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "364b6925-f6c6-4361-89e3-5144ad8d2ecc")]
    public readonly InputSlot<T3.Core.DataTypes.Point[]> Input = new();
}
