namespace Lib.mesh.modify;

[Guid("0f61b638-7bda-4331-944a-50fdca401223")]
internal sealed class FlipNormals : Instance<FlipNormals>
{
    [Output(Guid = "83268faa-5360-43af-9f85-eaec02574272")]
    public readonly Slot<MeshBuffers> Result = new();
        

    [Input(Guid = "89400186-9c36-4ba6-ac8a-55b7801fb99a")]
    public readonly InputSlot<MeshBuffers> Mesh = new();
}