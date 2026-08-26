namespace Lib.particle.force;

[Guid("1c9e27e9-7df0-4cda-ac4e-408f4516cf09")]
internal sealed class VelocityForce : Instance<VelocityForce>
{

    [Output(Guid = "12bd4849-f477-4b0a-b0bc-3e953d9f5888")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "d8d6645f-d295-4bb4-b514-4d7c0a982803")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "3b604264-3aa8-4f80-9171-4f719258f786")]
        public readonly InputSlot<float> Accelerate = new InputSlot<float>();

        [Input(Guid = "fde225b2-8e50-4d49-9f41-59acabda8c41")]
        public readonly InputSlot<float> Variation = new InputSlot<float>();

        [Input(Guid = "578ef158-2ed7-4cd9-8730-8d3f8a13f3e8")]
        public readonly InputSlot<System.Numerics.Vector2> VariationGainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "20a0b124-321e-450c-a064-3659eead0b45")]
        public readonly InputSlot<float> MinSpeed = new InputSlot<float>();

        [Input(Guid = "ad33ce12-6c79-4285-afd6-eff31799e955")]
        public readonly InputSlot<float> MaxSpeed = new InputSlot<float>();
}