namespace Examples.Lib.physics;

/// <summary>
/// GPU meshes as physics bodies: tori, icosahedra and cylinders rain into a pile.
/// </summary>
[Guid("bad6b43d-5448-5212-9480-b39a365d0d86")]
internal sealed class MeshToGeometryExample : Instance<MeshToGeometryExample>
{
    [Output(Guid = "037f33a9-733f-5e6f-8526-5502955aff28")]
    public readonly Slot<Texture2D> Output = new();
}
