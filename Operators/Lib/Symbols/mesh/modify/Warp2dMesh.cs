namespace Lib.mesh.modify;

[Guid("99435f7a-4969-4a0e-83f5-404ee6a0cfa2")]
internal sealed class Warp2dMesh : Instance<Warp2dMesh>
{

    [Output(Guid = "ee5ce4f1-a518-4545-b2ce-64005fade7a8")]
    public readonly Slot<MeshBuffers> Result = new();

    [Input(Guid = "8cf1ef77-4b59-412b-992b-74f88bf857f0")]
    public readonly InputSlot<MeshBuffers> InputMesh = new();

    [Input(Guid = "0fc8009e-8555-475e-a87c-5af5093a1cb9")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "9d00b18c-46f4-446c-8953-2972d90d9685")]
    public readonly InputSlot<BufferWithViews> TargetPoints = new();

        
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