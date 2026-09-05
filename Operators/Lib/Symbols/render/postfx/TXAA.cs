namespace Lib.render.postfx;

[Guid("22bb4788-d97a-4457-b6d1-1fc9ffe4f9ff")]
internal sealed class TXAA : Instance<TXAA>
{
    [Output(Guid = "bb809d80-6e56-4d3f-b055-85a5ce2f0564")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "19b07ddf-ba1e-4f8b-b476-e93485348e91")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "24b786f7-75d2-47d4-96e8-f7fcf0cc9258")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "414b89a2-128c-40c1-a3c7-137a788bb320")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "5d6e7f80-91a2-43b4-8506-7d8e9f011223")]
    public readonly InputSlot<float> BlendFactor = new();

    [Input(Guid = "6e7f8091-a2b3-44c5-9607-8e9f01122334")]
    public readonly InputSlot<float> NeighborhoodClamp = new();

    [Input(Guid = "7f8091a2-b3c4-45d6-a708-9f0112233445")]
    public readonly InputSlot<float> MotionThreshold = new();

    [Input(Guid = "8091a2b3-c4d5-46e7-b809-011223344556")]
    public readonly InputSlot<float> DepthRejection = new();

    [Input(Guid = "91a2b3c4-d5e6-47f8-8910-112233445566")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "6e95bd44-ebf7-44a3-b98e-dea871a676a4")]
    public readonly InputSlot<int> SampleCount = new();
}
