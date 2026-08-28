namespace Lib.point.transform;

[Guid("b263a6a1-0872-4223-80e7-5e09f4aea19d")]
internal sealed class WrapPoints : Instance<WrapPoints>
{

    [Output(Guid = "189921cd-cc7b-4d26-83b5-726815d3617c")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "4d74f19f-0f8b-4918-9999-8ae980e33d39")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "67f6eca6-baaa-48c8-8e9a-c25718ca94f5")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "daba3196-b9eb-4cb0-b062-00626cadc28b")]
    public readonly InputSlot<Vector3> Size = new();


    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
        WorldSpace,
    }
}