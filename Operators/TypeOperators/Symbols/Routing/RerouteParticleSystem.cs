namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.ParticleSystem through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("6ce0b457-a437-4dd6-af75-68499f7498cd")]
public sealed class RerouteParticleSystem : Instance<RerouteParticleSystem>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "a1557570-e15d-4b68-9015-e243c1fca933")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "73d7bb79-96dd-4e8f-b7b9-ac3387e8c898")]
    public readonly InputSlot<T3.Core.DataTypes.ParticleSystem> Input = new();
}
