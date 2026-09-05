namespace Lib.particle.force;

[Guid("543a97fb-8319-4767-a248-d0662d2e8781")]
internal sealed class VerletRibbonForce : Instance<VerletRibbonForce>
{
    [Output(Guid = "33f934f3-7e37-4d27-bf2e-6bc8698b9e7f")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

        [Input(Guid = "2d671320-2f3f-412d-97eb-34e1f4c5e75f")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "31b66f8a-ffa7-4951-95f1-f2b10827f4c1")]
        public readonly InputSlot<float> SegmentLength = new InputSlot<float>();

        [Input(Guid = "3811dd84-3221-467f-8e28-4afb7e677004")]
        public readonly InputSlot<System.Numerics.Vector3> Direction = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "dfd31cce-0b26-4259-ab16-ffaf5656c275")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> ReferencePoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "0ef80341-319f-4b14-b417-8b8d7524a1b9")]
        public readonly InputSlot<int> ConstrainPasses = new InputSlot<int>();

        [Input(Guid = "53f0d112-f4fb-434b-8650-65f392086f12")]
        public readonly InputSlot<float> RestoreFactor = new InputSlot<float>();

        [Input(Guid = "5d2bf09b-b582-4fd2-b006-94ac7a3fe6fb")]
        public readonly InputSlot<float> Damping = new InputSlot<float>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}