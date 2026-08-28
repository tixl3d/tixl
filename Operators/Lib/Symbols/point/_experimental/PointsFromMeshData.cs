namespace Lib.point._experimental;

[Guid("7d302c05-9898-4c56-a894-1f8f44b9b920")]
internal sealed class PointsFromMeshData : Instance<PointsFromMeshData>
{

    [Output(Guid = "b5907b75-97f7-484a-8bb1-5e81a0fd114d")]
    public readonly Slot<BufferWithViews> Points = new();

    [Input(Guid = "1f4184f4-c186-43f4-9c01-e7af9c2e4920")]
    public readonly InputSlot<ShaderResourceView> Data = new();

    [Input(Guid = "0f0652a0-8f5d-4f5c-ba1a-5bd3bd9a8f44")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "e62d9cfd-7dee-48ff-b1de-6e2c5cb3a31a")]
    public readonly InputSlot<float> Seed = new();
}