namespace Lib.particle.force;

[Guid("0f1bf849-949e-4462-a7af-ecb2ff3cd109")]
internal sealed class DirectionalForce : Instance<DirectionalForce>
{
    [Output(Guid = "3039b9df-2f48-47b7-98cf-2ca088c590a9")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

    [Input(Guid = "bcfe965f-27bd-4568-b25b-6987a26b5d6e")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

    [Input(Guid = "56b551e9-47aa-4d19-954b-367c4d96e5d8")]
    public readonly InputSlot<Vector3> Direction = new InputSlot<Vector3>();

    [Input(Guid = "fc7131da-a2d1-49c2-bcf7-ebc409347cb6")]
    public readonly InputSlot<float> RandomAmount = new InputSlot<float>();

    [Input(Guid = "d69efb70-71d7-4628-bd27-249f43f34676")]
    public readonly InputSlot<GizmoVisibility> ShowGizmo = new InputSlot<GizmoVisibility>();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}