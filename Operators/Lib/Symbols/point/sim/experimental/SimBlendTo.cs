namespace Lib.point.sim.experimental;

[Guid("4ba8019b-f265-4e41-8722-4ee0c2c64ba9")]
internal sealed class SimBlendTo : Instance<SimBlendTo>
{

    [Output(Guid = "fbee285f-3954-4321-9178-8f870698367f")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "1295b412-d9db-4805-9d28-0e3c7fc6e08c")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "3ed0658d-8193-4225-85ca-9663a2980c21")]
    public readonly InputSlot<BufferWithViews> PointsB = new();

    [Input(Guid = "183fee93-ce5b-4e65-9fa6-74e28b102ae2")]
    public readonly InputSlot<float> BlendFactor = new();

    [Input(Guid = "14c297b7-9bc6-4fda-84e1-40ac6a574d2b")]
    public readonly InputSlot<int> PairingMethod = new();
}