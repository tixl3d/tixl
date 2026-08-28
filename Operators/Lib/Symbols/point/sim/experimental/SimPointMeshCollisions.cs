namespace Lib.point.sim.experimental;

[Guid("281b69e8-01ee-4a62-a9ed-8508c5355c6c")]
internal sealed class SimPointMeshCollisions : Instance<SimPointMeshCollisions>
{

    [Output(Guid = "e6e49b7a-1510-47e8-a51a-7b684b4d1c1c")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "8f2d6bb8-5242-4f21-b356-7785b6b0ff4e")]
    public readonly InputSlot<BufferWithViews> PointsA_ = new();

    [Input(Guid = "61c55a9d-a2b9-4d60-84f4-ffd639d0d724")]
    public readonly InputSlot<float> Bouncyness = new();

    [Input(Guid = "7bc189be-25c6-44f2-b787-9e06d3dfa0b8")]
    public readonly InputSlot<float> ClampAccelleration = new();

    [Input(Guid = "d164bae5-b843-4378-ace6-d28ea04a3020")]
    public readonly InputSlot<float> Damping = new();

    [Input(Guid = "7a42dc58-f1fc-4473-abc3-5772817cb4b6")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "a4f2c0e6-1011-4793-933b-b9f0a332bbba")]
    public readonly InputSlot<MeshBuffers> Mesh = new();
}