namespace Lib.particle.force._;

[Guid("8342d554-6e83-49b1-91b5-2d4b5b63e726")]
internal sealed class RandomJumpForce : Instance<RandomJumpForce>
{

    [Output(Guid = "8e0066a8-7dbc-4d9d-8811-3eb2791a392e")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "f0f09ff2-7a42-470d-9c06-8d50e6a30ea4")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> ValueField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "c42bd78e-b9e2-4f39-a419-4ee8b482a542")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "81bed221-fa12-4373-b485-0fd237a62304")]
        public readonly InputSlot<float> Frequency = new InputSlot<float>();

        [Input(Guid = "17758594-7c61-4524-a83f-7489daeb5005")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "29df5011-af91-498d-b2c3-0f73d86f6e51")]
        public readonly InputSlot<float> Variation = new InputSlot<float>();

        [Input(Guid = "cdfb619d-8235-4f46-bdf1-b97900832efc")]
        public readonly InputSlot<float> AmountFromVelocity = new InputSlot<float>();

        [Input(Guid = "e5fc6af0-e70e-46b5-b19b-86cd21e76e89")]
        public readonly InputSlot<System.Numerics.Vector3> DirectionDistribution = new InputSlot<System.Numerics.Vector3>();
}