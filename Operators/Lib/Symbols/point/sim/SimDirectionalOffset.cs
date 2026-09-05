namespace Lib.point.sim;

[Guid("7a08d73e-1aea-479f-8d36-ecb119d75c3a")]
internal sealed class SimDirectionalOffset : Instance<SimDirectionalOffset>
{

    [Output(Guid = "3517d466-d084-45e4-885a-8c7f6b16446e")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "82746dde-ab65-4c49-a22f-cf9a50f4a3e9")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "1840e5b8-2aee-44d0-b826-d34395325506")]
    public readonly InputSlot<Vector3> Direction = new();

    [Input(Guid = "4f9ab443-3885-4a57-9116-6be5824bd95b")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "2fe652d7-92c7-4cd0-8190-78905be178f1")]
    public readonly InputSlot<float> RandomAmount = new();

    [Input(Guid = "1f561bbd-a272-4c06-bd56-a580a2022bc6", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "a6b0a4da-2f6f-4941-b097-ed5e3dd6af0b")]
    public readonly InputSlot<GizmoVisibility> ShowGizmo = new();
        
        
    private enum Modes {
        Legacy,
        EncodeInRotation,
    }
}