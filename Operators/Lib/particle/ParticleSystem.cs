namespace Lib.particle;

[Guid("e44ae9b6-cd56-4224-8a5d-118eda4cd3f4")]
internal sealed class ParticleSystem : Instance<ParticleSystem>
{

    [Output(Guid = "51b9c6bd-b7cc-48a4-979b-3febcac914c2")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

        [Input(Guid = "ba08e719-a1d1-4ac6-9c8c-076478a65a81")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> EmitPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "7a320c47-14ed-4637-928b-25f87bd32c26")]
        public readonly InputSlot<bool> Emit = new InputSlot<bool>();

        [Input(Guid = "c6bdbb07-6996-4e5b-a058-37c9cbeca6fe")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "cab3072c-694e-4278-9b1e-e30beb95e5c8")]
        public readonly InputSlot<bool> Update = new InputSlot<bool>();

        [Input(Guid = "1eb32e4c-6785-4649-b0bf-7c70cddee619")]
        public readonly InputSlot<int> MaxParticleCount = new InputSlot<int>();

        [Input(Guid = "38392c86-4a1b-4b6f-ac32-26920a73e1e2")]
        public readonly InputSlot<float> EmitVelocity = new InputSlot<float>();

        [Input(Guid = "54b8bfc1-98b1-4cec-b008-8615c20ebb43", MappedType = typeof(StrengthFactors))]
        public readonly InputSlot<int> EmitVelocityFactor = new InputSlot<int>();

        [Input(Guid = "b2c8f7cf-fdf5-4819-98ec-2c70ee9e8bc6")]
        public readonly InputSlot<float> RadiusFactor = new InputSlot<float>();

        [Input(Guid = "21c666bb-a28f-498c-a834-a2ba4aca78a7", MappedType = typeof(EmitModes))]
        public readonly InputSlot<int> EmitMode = new InputSlot<int>();

        [Input(Guid = "4ca2f43a-ed90-4388-ae6f-2687e85db5a6")]
        public readonly InputSlot<float> LifeTime = new InputSlot<float>();

        [Input(Guid = "9642f5c6-5ad2-4d35-a5ed-a3fde10817ae")]
        public readonly InputSlot<float> Speed = new InputSlot<float>();

        [Input(Guid = "4b0ccec5-b72e-4834-80d4-77225f30d2a9")]
        public readonly InputSlot<float> OrientTowardsVelocity = new InputSlot<float>();

        [Input(Guid = "5a61994e-42c7-47e7-b0a5-5beb48f4a34b")]
        public readonly InputSlot<float> Drag = new InputSlot<float>();

        [Input(Guid = "a7350ba8-08be-4afc-92f1-d223ee9bcbeb", MappedType = typeof(SetWModes))]
        public readonly InputSlot<int> SetF1To = new InputSlot<int>();

        [Input(Guid = "4c555ac3-3d62-47a7-9d1a-49e4d5808635", MappedType = typeof(SetWModes))]
        public readonly InputSlot<int> SetF2To = new InputSlot<int>();

        [Input(Guid = "c41d9633-1397-4602-a5f8-7808c3d63108")]
        public readonly MultiInputSlot<T3.Core.DataTypes.ParticleSystem> ParticleForces = new MultiInputSlot<T3.Core.DataTypes.ParticleSystem>();
        
    private enum SetWModes {
        KeepOriginal,
        ParticleAge,
        ParticleSpeed,
    }
        
    private enum EmitModes {
        Sequential,
        ForLines,
    }
    
    private enum StrengthFactors
    {
        None = 0,
        F1 = 1,
        F2 = 2,
    }
}