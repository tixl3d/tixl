namespace Lib.point.modify;

[Guid("0f40e5e5-e406-4f87-854b-fbdd670b5504")]
internal sealed class SamplePointsByCameraDistance : Instance<SamplePointsByCameraDistance>
{

    [Output(Guid = "7aeca2d3-c8aa-421f-91df-5a9df06a3040")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "57b87561-626c-44a9-ac81-393ede887c67")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "f03533f6-eec4-4cce-9736-b751322efa26")]
    public readonly InputSlot<float> NearRange = new();

    [Input(Guid = "a7f46f47-4cc1-4d62-ae23-46b12aa44eea")]
    public readonly InputSlot<float> FarRange = new();

    [Input(Guid = "4b41cfe4-303e-4748-a585-45babdf18e0e")]
    public readonly InputSlot<Curve> WForDistance = new();
}