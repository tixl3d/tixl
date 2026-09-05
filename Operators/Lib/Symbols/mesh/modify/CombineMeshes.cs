namespace Lib.mesh.modify;

[Guid("c276f4eb-f19c-405b-b247-3db159677571")]
internal sealed class CombineMeshes : Instance<CombineMeshes>
{

    [Output(Guid = "a1303162-1a8e-4dfc-bcb9-644265530742")]
    public readonly Slot<MeshBuffers> CombinedMesh = new();

    [Input(Guid = "815b313e-e661-43db-b788-54cf824c0d8a")]
    public readonly MultiInputSlot<MeshBuffers> Meshes = new();

    [Input(Guid = "c0bf8ff1-e578-41c2-b4df-40359cb48609")]
    public readonly InputSlot<bool> IsEnabled = new();
}