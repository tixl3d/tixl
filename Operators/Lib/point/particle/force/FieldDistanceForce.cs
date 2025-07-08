using T3.Core.DataTypes.ShaderGraph;

namespace Lib.point.particle.force;

[Guid("42394232-51fa-4e75-851b-c2bca39de71a")]
internal sealed class FieldDistanceForce : Instance<FieldDistanceForce>
{
    [Output(Guid = "90e8bd09-857a-4de0-b7a6-ab2be17af8ae")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "43e90070-5841-441c-8658-7854b80003b9")]
        public readonly InputSlot<ShaderGraphNode> Field = new InputSlot<ShaderGraphNode>();

        [Input(Guid = "3fd1169e-1640-4d67-b859-70815cb3d28f")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "be2df23e-d32c-4cfe-947b-352113ccdff8")]
        public readonly InputSlot<float> RandomizeSpeed = new InputSlot<float>();

        [Input(Guid = "d7912224-7736-417c-ae27-acc9e74067ca")]
        public readonly InputSlot<float> SurfaceDistance = new InputSlot<float>();

        [Input(Guid = "8145f581-65bf-4965-81e9-2aed3622039f")]
        public readonly InputSlot<float> RandomSurfaceDistance = new InputSlot<float>();

        [Input(Guid = "1b53c8e9-0330-43c8-b684-54e982c9f706")]
        public readonly InputSlot<float> Spin = new InputSlot<float>();

        [Input(Guid = "00b5006c-1d22-4ce2-958e-ad7e002e7821")]
        public readonly InputSlot<float> RandomSpin = new InputSlot<float>();

        [Input(Guid = "94d3ad87-0509-4fd4-9644-e2cb557f6789")]
        public readonly InputSlot<float> RandomPhase = new InputSlot<float>();

        [Input(Guid = "6e1099f2-78d7-4807-ba06-6b5354fb6ac9")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}