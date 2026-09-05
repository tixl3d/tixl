namespace Lib.point.generate;

[Guid("69bd6cd3-85b2-4a6c-9184-ab25045a51aa")]
internal sealed class BoundingBoxPoints : Instance<BoundingBoxPoints>
{

    [Output(Guid = "921d43ce-6167-409b-9748-d6f59daa1cde")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "c6082d55-5e47-404f-96f9-612959cd75ce")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();


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