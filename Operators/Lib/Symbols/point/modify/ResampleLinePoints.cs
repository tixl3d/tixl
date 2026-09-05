namespace Lib.point.modify;

[Guid("13ff9adb-2634-4129-8bb4-4fb764d38be6")]
internal sealed class ResampleLinePoints : Instance<ResampleLinePoints>
{

    [Output(Guid = "28cba376-7037-4d8c-bc4b-a8c747687f03")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "78f5d842-960f-4885-a65b-defd04871091")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();

    [Input(Guid = "e731ef71-b172-4308-b7d2-a59fa55b266a")]
    public readonly InputSlot<int> Count = new InputSlot<int>();

    [Input(Guid = "58980e30-204b-40e2-9610-8482ff01a57c", MappedType = typeof(SampleModes))]
    public readonly InputSlot<int> RangeMode = new InputSlot<int>();

    [Input(Guid = "354e468d-d38a-49ba-b2f3-8e522723d43f")]
    public readonly InputSlot<Vector2> SampleRange = new InputSlot<Vector2>();

    [Input(Guid = "3d50d3c5-07e6-4246-8740-fcdc62173e1d")]
    public readonly InputSlot<float> SmoothDistance = new InputSlot<float>();

    [Input(Guid = "aba0b64e-5438-41a6-8421-8820024ed329")]
    public readonly InputSlot<int> Samples = new InputSlot<int>();

    [Input(Guid = "3e2be2bd-ffe9-4758-828d-5d6f4e1f1581", MappedType = typeof(RotationModes))]
    public readonly InputSlot<int> Rotation = new InputSlot<int>();

    [Input(Guid = "14524523-801d-4c70-9f42-af4f8d37be8a")]
    public readonly InputSlot<Vector3> RotationUpVector = new InputSlot<Vector3>();


    private enum SampleModes
    {
        StartEnd,
        StartLength,
    }

    private enum RotationModes
    {
        Interpolate,
        Recompute,
    }
}