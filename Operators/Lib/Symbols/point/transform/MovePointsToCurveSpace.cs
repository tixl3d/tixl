namespace Lib.point.transform;

[Guid("ac185a9e-02c4-4ee0-b55e-4e7384c09d0c")]
internal sealed class MovePointsToCurveSpace : Instance<MovePointsToCurveSpace>
{

    [Output(Guid = "d47c5841-f33a-4b4f-b969-4d56c7fb7446")]
    public readonly Slot<BufferWithViews> ResultPoints = new();

    [Input(Guid = "6e5bc8a8-fac6-450c-826e-188bb20ada43")]
    public readonly InputSlot<BufferWithViews> SourcePoints = new InputSlot<BufferWithViews>();

    [Input(Guid = "000e00d8-4f53-4a39-abd9-d2495ffff11d")]
    public readonly InputSlot<BufferWithViews> TargetPoints = new InputSlot<BufferWithViews>();

    [Input(Guid = "b7ebf5a0-bd74-412d-a70f-c8c2fe01d108")]
    public readonly InputSlot<Vector3> Extent = new InputSlot<Vector3>();

    [Input(Guid = "ad797f3a-296d-4170-99ba-a27422f0d909")]
    public readonly InputSlot<Vector3> Pivot = new InputSlot<Vector3>();

    [Input(Guid = "8cb0597f-1d57-4cc1-8079-0ad73dacf491", MappedType = typeof(SourceAlignments))]
    public readonly InputSlot<int> SourceAlignment = new InputSlot<int>();

    [Input(Guid = "7169ac45-9863-4557-bf7c-1040f732416d")]
    public readonly InputSlot<float> Range = new InputSlot<float>();

    [Input(Guid = "51d0366c-fd0a-4385-86e2-729a06ba8430")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "7b1cb7b0-110f-4c13-9f85-ab3a205ba633")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "921a72d9-9f31-4433-a7b8-b9b4aac365f4", MappedType = typeof(RepeatModes))]
    public readonly InputSlot<int> AtBoundaries = new InputSlot<int>();

    [Input(Guid = "b5f78276-986e-45ca-b360-c4e40c338157")]
    public readonly InputSlot<GizmoVisibility> Visibility = new InputSlot<GizmoVisibility>();

        
    private enum SourceAlignments
    {
        X,
        Y,
        Z
    }
        
    private enum RepeatModes
    {
        Extend,
        Wrap,
        Clamp,
    }
}