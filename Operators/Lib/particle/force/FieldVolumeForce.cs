namespace Lib.particle.force;

[Guid("599ada75-b2ac-4f6c-ac98-f7b4bb6cf47d")]
internal sealed class FieldVolumeForce : Instance<FieldVolumeForce>
{
    [Output(Guid = "3296dd74-7e80-459d-8b4d-6127437d73d8")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "e24a0dca-504f-4fa9-8a1d-272611506613")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "86f4bbf4-26a4-4dd7-9a23-3ef1a6c5a13b")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "02eb5c6b-3995-409b-bd18-9e5c3d58364a")]
        public readonly InputSlot<float> Attraction = new InputSlot<float>();

        [Input(Guid = "dba5f4e3-cbdc-4e12-b174-dd9b7d91bea8")]
        public readonly InputSlot<float> AttractionDecay = new InputSlot<float>();

        [Input(Guid = "0f929afe-8ba0-479f-adae-d96f89f86266")]
        public readonly InputSlot<float> Repulsion = new InputSlot<float>();

        [Input(Guid = "8eac0a70-00ad-45cf-a72a-d58593475151")]
        public readonly InputSlot<float> Bounciness = new InputSlot<float>();

        [Input(Guid = "57ddd456-78f1-486c-a28f-9cfc02f8a989")]
        public readonly InputSlot<float> RandomizeBounce = new InputSlot<float>();

        [Input(Guid = "97334c3a-7d1b-4990-a541-70f6ef040197")]
        public readonly InputSlot<bool> ReflectOnCollision = new InputSlot<bool>();

        [Input(Guid = "212064e0-7adb-467d-91d7-9b8890d854bc")]
        public readonly InputSlot<float> RandomizeReflection = new InputSlot<float>();

        [Input(Guid = "bf4f4938-e8ca-4b64-aa8b-6247527ad2fd")]
        public readonly InputSlot<bool> InvertVolume = new InputSlot<bool>();

        [Input(Guid = "637f406f-7cd2-496b-89b1-13945c14f637")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();

        [Input(Guid = "64fb0a6f-8a7b-4121-ae71-3b028953df28")]
        public readonly InputSlot<bool> ApplyColorOnCollision = new InputSlot<bool>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}