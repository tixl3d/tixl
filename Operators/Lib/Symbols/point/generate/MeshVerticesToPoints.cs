namespace Lib.point.generate;

[Guid("2467e1ed-f7fc-4c90-8230-b80ba6b42a2d")]
internal sealed class MeshVerticesToPoints : Instance<MeshVerticesToPoints>
{

    [Output(Guid = "53089fc7-3f0b-46c4-81e1-04ecbb92efce")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "b990cf29-00a5-4e39-8687-4502c7c7eebc")]
    public readonly InputSlot<MeshBuffers> Mesh = new InputSlot<MeshBuffers>();

    [Input(Guid = "e5ab7ae6-d8de-4c92-9130-1082e5a56ba1")]
    public readonly InputSlot<float> W = new InputSlot<float>();

    [Input(Guid = "664b9a97-0709-40d5-b0a0-651092e658af")]
    public readonly InputSlot<Vector3> OffsetByTBN = new InputSlot<Vector3>();
}