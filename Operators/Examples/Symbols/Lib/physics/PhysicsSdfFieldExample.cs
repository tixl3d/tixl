namespace Examples.Lib.physics;

/// <summary>
/// Raymarched SDF terrain and a moving blob that physics bodies and meshes collide with.
/// </summary>
[Guid("ae28dc10-527b-5b7e-8f97-099aacc5aa79")]
internal sealed class PhysicsSdfFieldExample : Instance<PhysicsSdfFieldExample>
{
    [Output(Guid = "91f35899-6116-5702-b61b-d310de0bce90")]
    public readonly Slot<Texture2D> Output = new();
}
