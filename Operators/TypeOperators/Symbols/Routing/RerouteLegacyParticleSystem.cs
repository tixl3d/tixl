namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.LegacyParticleSystem through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("2a96d838-4966-429c-96eb-22841855fe70")]
public sealed class RerouteLegacyParticleSystem : Instance<RerouteLegacyParticleSystem>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "8fe96ab6-8c50-486b-9712-7583670177d5")]
    public readonly Slot<T3.Core.DataTypes.LegacyParticleSystem> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "9300864c-e471-45b3-840a-49d177d2525d")]
    public readonly InputSlot<T3.Core.DataTypes.LegacyParticleSystem> Input = new();
}
