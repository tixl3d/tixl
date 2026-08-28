namespace Lib.point.sim.experimental;

[Guid("03aa5f28-3f74-4feb-aa6a-36cdb2d7f0d9")]
internal sealed class SimFollowMeshSurface : Instance<SimFollowMeshSurface>
{

    [Output(Guid = "124342ba-0117-4969-90d4-8085a9a42e52")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "72906b23-c042-4880-a180-83b8fc414bb2")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "f13d61c1-c16b-44a4-bc29-471c2dff5a4c")]
    public readonly InputSlot<float> Speed = new();

    [Input(Guid = "a63907ec-606c-4e56-aa16-6186678a1868")]
    public readonly InputSlot<float> RandomSpeed = new();

    [Input(Guid = "1bf08002-beee-4ba4-b91f-c67777839ec3")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "530c2b66-eb32-47ee-be7f-b257328d841e")]
    public readonly InputSlot<float> RandomSpin = new();

    [Input(Guid = "73e0911b-a10b-44aa-a8b3-7a31c4d2180b")]
    public readonly InputSlot<float> SurfaceDistance = new();

    [Input(Guid = "09e53487-35d0-4397-a678-3f7773ed1d0d")]
    public readonly InputSlot<float> ScatterSurfaceDistance = new();

    [Input(Guid = "cd86f4c4-7189-450f-a383-62bc4967366b")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "5f3f2dee-bfbe-449b-b5f8-0a0cc79049bc")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "25dc9800-a7ea-4c28-b76c-829346da3ed0")]
    public readonly InputSlot<float> Phase = new();
}