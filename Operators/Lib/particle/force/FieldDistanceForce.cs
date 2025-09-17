namespace Lib.particle.force;

[Guid("42394232-51fa-4e75-851b-c2bca39de71a")]
internal sealed class FieldDistanceForce : Instance<FieldDistanceForce>
{
    [Output(Guid = "90e8bd09-857a-4de0-b7a6-ab2be17af8ae")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "43e90070-5841-441c-8658-7854b80003b9")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "8145f581-65bf-4965-81e9-2aed3622039f")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "3fd1169e-1640-4d67-b859-70815cb3d28f")]
        public readonly InputSlot<float> Attraction = new InputSlot<float>();

        [Input(Guid = "be2df23e-d32c-4cfe-947b-352113ccdff8")]
        public readonly InputSlot<float> Repulsion = new InputSlot<float>();

        [Input(Guid = "6e1099f2-78d7-4807-ba06-6b5354fb6ac9")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();

        [Input(Guid = "fd65a976-577a-4c33-9660-ad7fac92c5f6")]
        public readonly InputSlot<float> DecayWithDistance = new InputSlot<float>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}