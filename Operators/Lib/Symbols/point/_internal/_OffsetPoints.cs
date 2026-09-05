namespace Lib.point._internal;

[Guid("3737cd30-c79a-4282-897a-7d2a44076c65")]
internal sealed class _OffsetPoints : Instance<_OffsetPoints>
{

    [Output(Guid = "5a0777ae-9dff-4c8f-b206-eac6d65a910f")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "4b7cc2cc-8f7b-4460-8beb-8a4eea101ef6")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "a17861cd-41e8-4cbb-9119-74e091bf4de1")]
    public readonly InputSlot<Vector3> Direction = new();

    [Input(Guid = "eb6318b0-619e-47ef-ae3b-fc760137f306")]
    public readonly InputSlot<float> Distance = new();
}