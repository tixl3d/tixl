namespace Lib.mesh.modify;

[Guid("3f0f0c40-482d-4d79-a201-b4651a0cd3a8")]
internal sealed class SplitMeshVertices : Instance<SplitMeshVertices>
{

    [Output(Guid = "7873a4c0-04ff-41b5-bf5e-66ae745c3918")]
    public readonly Slot<MeshBuffers> Result = new();

    [Input(Guid = "22370faa-8381-4878-8653-2fe9297400da")]
    public readonly InputSlot<MeshBuffers> InputMesh = new();

    [Input(Guid = "308f12dc-a308-472a-923e-20f0a20d54db")]
    public readonly InputSlot<float> ShadeFlat = new();

        
    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
        WorldSpace,
    }
        
    private enum Directions
    {
        WorldSpace,
        SurfaceNormal,
    }
}