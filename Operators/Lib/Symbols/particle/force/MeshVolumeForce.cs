namespace Lib.particle.force;

[Guid("c3cdcbed-7d2e-4f2f-8725-bf18bc4a2b73")]
internal sealed class MeshVolumeForce :Instance<MeshVolumeForce>{
    [Output(Guid = "dedbd7bd-d593-406d-a6cc-cd9d409bbe8c")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "9709b1c0-7b4f-46cc-93bf-7405564b514a")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "b4a1c048-f6e1-424e-b63d-c3a1896bffb4")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "ea8154a0-b78c-4a62-92a2-1878e45dba77")]
        public readonly InputSlot<float> Attraction = new InputSlot<float>();

        [Input(Guid = "b8cea063-6c58-4453-ba6d-7c7b6c7ca7f9")]
        public readonly InputSlot<float> AttractionDecay = new InputSlot<float>();

        [Input(Guid = "145cf7e8-3120-40f4-ab4a-a23c5dd27ff6")]
        public readonly InputSlot<float> Repulsion = new InputSlot<float>();

        [Input(Guid = "4bd6bcc0-02f6-48f6-8fa0-87d6baca1f4d")]
        public readonly InputSlot<float> Bounciness = new InputSlot<float>();

        [Input(Guid = "5b877467-8a24-4feb-a491-13c1a208cc40")]
        public readonly InputSlot<float> RandomizeBounce = new InputSlot<float>();

        [Input(Guid = "710246cb-2b6d-464c-9e95-345fa7127cb9")]
        public readonly InputSlot<bool> ReflectOnCollision = new InputSlot<bool>();

        [Input(Guid = "e31cfff8-0b9e-4c72-92a4-d89a6db40a23")]
        public readonly InputSlot<float> RandomizeReflection = new InputSlot<float>();

        [Input(Guid = "95b532a1-23b1-4e83-88e6-f69e7efafb4a")]
        public readonly InputSlot<bool> InvertVolume = new InputSlot<bool>();

        [Input(Guid = "9e3e295d-ed8a-41b5-91ba-3cd06261b9a2")]
        public readonly InputSlot<bool> ApplyColorOnCollision = new InputSlot<bool>();

        [Input(Guid = "f81826f1-391c-48b5-86d4-807d1654ca22")]
        public readonly InputSlot<int> ExecuteEveryNthFrame = new InputSlot<int>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}