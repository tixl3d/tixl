namespace Types.Routing;

[Guid("2a96d838-4966-429c-96eb-22841855fe70")]
public sealed class RerouteLegacyParticleSystem : Instance<RerouteLegacyParticleSystem>, IRerouteNode
{
    [Output(Guid = "8fe96ab6-8c50-486b-9712-7583670177d5")]
    public readonly Slot<T3.Core.DataTypes.LegacyParticleSystem> Output = new();

    [Input(Guid = "9300864c-e471-45b3-840a-49d177d2525d")]
    public readonly InputSlot<T3.Core.DataTypes.LegacyParticleSystem> Input = new();
}
