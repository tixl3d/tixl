namespace Lib.point.generate;

[Guid("25db2a97-38b2-4503-8842-fab3922d7a6c")]
internal sealed class PointTrailFast : Instance<PointTrailFast>
{

    [Output(Guid = "6e3ca38f-78d6-4e2b-b8ab-10a906e058e2")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "4389838c-fd1d-4400-b8e5-f373a05adff7")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "6a2cb3f0-3b5a-4551-a809-3dc172bb7d79")]
    public readonly InputSlot<int> TrailLength = new();

    [Input(Guid = "bcb7260e-8a84-4987-83ca-f31981ae94aa")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "63621a98-874b-4d1a-9724-f4fa70b8ccf1")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "56eac471-ad48-41ec-b617-cbcb67646c97")]
    public readonly InputSlot<float> AddSeperatorThreshold = new();
}