namespace Lib.point.transform;

[Guid("0814a593-80ab-416f-a3ca-eef78b0a9c0c")]
internal sealed class WrapPointPosition : Instance<WrapPointPosition>
{

    [Output(Guid = "2889b8bf-5bb2-48f8-8fe0-02f95282c5f1")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "dbe72c8b-6cc2-454b-83db-712f0cd4211c")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "fb569f81-c8d3-4330-8035-6bde4e0bd710")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "1d054b2e-1e1b-4899-a003-0d6e000d2d8d")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "8b09ea72-d6e8-444c-b20a-05133d846571")]
    public readonly InputSlot<bool> UseCameraPosition = new();

    [Input(Guid = "d56c770e-1cc5-4bab-8f2d-1b503e686aed")]
    public readonly InputSlot<bool> AddLineBreaks = new();
}