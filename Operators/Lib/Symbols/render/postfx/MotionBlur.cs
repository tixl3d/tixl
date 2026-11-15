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

    [Input(Guid = "8f1c6e2a-b374-4d59-9a0e-5c7d3b21f846")]
    public readonly InputSlot<Texture2D> VelocityMap = new();

    [Input(Guid = "d757058a-a31e-487f-b002-cc06bc478535")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "4e7b2d90-6a1c-4f38-b5d2-e09a8c3f7165")]
    public readonly InputSlot<float> Shutter = new();

    [Input(Guid = "b2a95f07-3d4e-4c61-8e7b-19f6d0c2a538")]
    public readonly InputSlot<float> MaxBlur = new();

    [Input(Guid = "df8bf08c-e576-4310-95b5-e34c6a001959")]
    public readonly InputSlot<int> SampleCount = new();
}
