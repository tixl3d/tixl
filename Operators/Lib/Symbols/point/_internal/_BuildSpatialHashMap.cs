namespace Lib.point._internal;

[Guid("f61ceb9b-74f8-4883-88ea-7e6c35b63bbd")]
internal sealed class _BuildSpatialHashMap : Instance<_BuildSpatialHashMap>
{
    [Output(Guid = "59d09aa6-051c-4906-9f32-f65e66979c56")]
    public readonly Slot<Command> Update = new ();
        
    [Output(Guid = "b4505f1e-ab0e-45be-8e46-8e3b37ec59ec")]
    public readonly Slot<ShaderResourceView> CellPointIndices = new();

    [Output(Guid = "6c026a5f-a724-4240-bb96-077d65266f66")]
    public readonly Slot<ShaderResourceView> PointCellIndices = new();

    [Output(Guid = "fb96e3d2-9a0f-466a-9b1d-997a4aa3e625")]
    public readonly Slot<ShaderResourceView> HashGridCells = new();

    [Output(Guid = "13f0d2c2-a18b-457b-a3cf-cfd0c755b9a9")]
    public readonly Slot<ShaderResourceView> CellPointCounts = new();

    [Output(Guid = "eeb282ee-ad73-471c-89ab-ae7cc8de6b15")]
    public readonly Slot<ShaderResourceView> CellRangeIndices = new();



    [Input(Guid = "4bae9eaa-42d8-4c1c-8776-3abebcce20e2")]
    public readonly InputSlot<BufferWithViews> PointsA_ = new();

    [Input(Guid = "22f9737b-b3b4-4455-a4ec-8d61ab7abc6c")]
    public readonly InputSlot<float> CellSize = new();
}