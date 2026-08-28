namespace Lib.point.modify;

[Guid("15f056a3-ee8b-41a2-92c9-eb85153f8200")]
internal sealed class _RandomizePoints_Legacy1 : Instance<_RandomizePoints_Legacy1>
{

    [Output(Guid = "92864bf1-5cc9-4e42-a136-e4f79282297a")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "06332b65-36c9-4dc8-a3db-64b2ee116148")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "5fc7dd7c-6298-4f7e-9e4e-cec3506c19aa")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "72308bda-fe03-429e-8f42-a975b11ca8a4")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "dedd7211-21c2-4fa0-88d3-14e7a062e7ab")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "3d045aa7-9f4f-4a42-8f12-a9dc30c30410")]
    public readonly InputSlot<float> RandomizeW = new();

    [Input(Guid = "8f8c14b3-e87e-4295-a591-9fa7ebcce8f3", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> Space = new();

    [Input(Guid = "1503C058-E270-40CD-99E6-40325B9233A7")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "07c73fd8-6126-4e03-8f10-05d28a3c7ef4")]
    public readonly InputSlot<float> Gain = new();

    [Input(Guid = "36e69f1c-412b-4a5c-8b17-d0f5d909f1cb")]
    public readonly InputSlot<float> Offset = new();

    [Input(Guid = "de5d13dd-6cdc-4639-95f8-34b6f91cfe78")]
    public readonly MultiInputSlot<bool> UseWAsSelection = new();

    [Input(Guid = "b2d8f5e0-56fc-4cb1-8bf2-b0f027749055")]
    public readonly InputSlot<float> RandomPhase = new();


    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
        WorldSpace,
    }
}