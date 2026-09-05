namespace Lib.particle.force;

[Guid("2cc44046-2702-40d5-ab42-8b36ff3d6ca7")]
internal sealed class AxisStepForce : Instance<AxisStepForce>
{

    [Output(Guid = "2b56d43b-1d92-4895-81f0-e30fdac5e6ef")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

    [Input(Guid = "7a0aaade-2cf4-45f8-85aa-1133278899ad")]
    public readonly InputSlot<bool> ApplyTrigger = new();

    [Input(Guid = "a28e97a1-8bcf-4701-a8bb-97d69f91bc4b")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "1f642c68-fca9-4552-bb23-9d066f4b2dda")]
    public readonly InputSlot<float> RandomizeStrength = new();

    [Input(Guid = "42501a2b-4b10-43ff-8774-1e34fb868417")]
    public readonly InputSlot<float> SelectRatio = new();

    [Input(Guid = "4701df38-3ec8-4133-ba40-afdf859e8f2f")]
    public readonly InputSlot<Vector3> AxisDistribution = new();

    [Input(Guid = "25e77db3-6ccb-4857-8dd6-e5c19395d85b")]
    public readonly InputSlot<float> AddOriginalVelocity = new();

    [Input(Guid = "f5e93296-efe1-42e0-add8-f8b6298fe183")]
    public readonly InputSlot<Vector3> StrengthDistribution = new();

    [Input(Guid = "ab591dbc-b521-4ef0-b1e2-d03ff27b959e", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> AxisSpace = new();

    [Input(Guid = "f6e3e1df-6f72-4c5f-a10e-771e5d7c9fa0")]
    public readonly InputSlot<int> Seed = new();

    private enum Spaces
    {
        ObjectSpace,
        RotationSpace,
    }
}