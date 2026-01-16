namespace Lib.particle.force;

[Guid("3f8376f2-b89a-4ab4-b6dc-a3e8bf88c0a5")]
internal sealed class TurbulenceForce : Instance<TurbulenceForce>
{

    [Output(Guid = "e5bbe22e-e3f6-4f1f-9db0-fc7632c10639", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

    [Input(Guid = "e27a97ce-3d0f-41a9-93c3-a1691f4029aa")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "f0345217-29f4-48f8-babd-8aed134cb0d5")]
    public readonly InputSlot<float> Frequency = new();

    [Input(Guid = "419b5ec5-8f6d-4c2d-a633-37d125cfcf07")]
    public readonly InputSlot<float> Phase = new();

    [Input(Guid = "56144ddb-9d4b-4e08-9169-7853a767f794")]
    public readonly InputSlot<float> Variation = new();

    [Input(Guid = "671a04f9-0f40-45ea-a2df-4f06c08d9647")]
    public readonly InputSlot<float> AmountFromVelocity = new();

        [Input(Guid = "f3d0c69e-3788-49e8-bd70-361c446b4d62")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ValueField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();
}