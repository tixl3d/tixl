namespace Lib.particle.force;

[Guid("658b6a44-ff5a-42ca-8673-33874b752004")]
internal sealed class VectorFieldForce : Instance<VectorFieldForce>
{
    [Output(Guid = "58fdb657-3667-478a-8a6b-8aa8fdf73b08")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "e095070a-835d-4d48-8ef3-543f1b77168b")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> VectorField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "27b2a405-e30c-4bc8-8058-468e6a806bd2")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "fb9d4bfa-6a4e-4647-861a-b8b5a5ca690d")]
        public readonly InputSlot<float> Randomize = new InputSlot<float>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}