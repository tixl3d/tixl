namespace Lib.render.postfx;

[Guid("196e14c7-3a71-4afd-a53b-e51d0fe8f414")]
internal sealed class MotionBlur : Instance<MotionBlur>
{
    [Output(Guid = "1b237829-8cfd-4039-a6c5-8ca3dbb225f7")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "619c2684-8495-4c19-a5b2-673728feaa00")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "3d99ccde-2bc3-4a25-962d-dab4fc6c554a")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "d757058a-a31e-487f-b002-cc06bc478535")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "df8bf08c-e576-4310-95b5-e34c6a001959")]
    public readonly InputSlot<int> SampleCount = new();

    [Input(Guid = "db0d11c8-f4c0-4762-8821-4dd75c71fb9e")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "77ed2341-48f2-4e0f-913d-b2f368449088")]
    public readonly InputSlot<float> ClampEffect = new();

    [Input(Guid = "2ac4c1a2-c53e-4b78-90fc-15c0b69c8b28")]
    public readonly InputSlot<Vector3> VelocityOffset = new InputSlot<Vector3>();

}