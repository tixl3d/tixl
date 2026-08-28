namespace Lib.mesh.modify;

[Guid("68e0d0cb-1e57-4e9c-9f22-bd7927ddb4c5")]
internal sealed class RecomputeNormals : Instance<RecomputeNormals>
{

    [Output(Guid = "69a94ae6-21f3-4c04-bb7d-98fb469463bb")]
    public readonly Slot<MeshBuffers> Result = new();

    [Input(Guid = "b55aeb9b-5286-476a-b8f0-86cb96e41310")]
    public readonly InputSlot<MeshBuffers> InputMesh = new();

    [Input(Guid = "fd3f8225-3d33-40d3-af15-ae768e2c67ad")]
    public readonly InputSlot<bool> RecomputeIndices = new();

        
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