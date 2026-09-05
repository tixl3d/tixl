namespace Lib.image.color;


[Guid("d9a71078-8296-4a07-b7de-250d4e2b95ac")]
internal sealed class Tint : Instance<Tint>
{
    [Output(Guid = "ce2fb7bd-6204-4b07-ab35-42bcb7aeaffe")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "3f1d8fa3-73bd-475c-a65b-c5352bf6ea85")]
    public readonly InputSlot<Texture2D> Texture2d = new InputSlot<Texture2D>();

    [Input(Guid = "7307d198-d2d5-41d4-b8b8-d2ece26dade8")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

    [Input(Guid = "387ce8fc-42c2-438d-bf52-144b5dfd8811")]
    public readonly InputSlot<Vector4> MapBlackTo = new InputSlot<Vector4>();

    [Input(Guid = "26a45300-c4d6-4e43-8550-37d2fa87799d")]
    public readonly InputSlot<Vector4> MapWhiteTo = new InputSlot<Vector4>();

    [Input(Guid = "07d592c1-7c5a-4c97-ba97-ebd229304dc8")]
    public readonly InputSlot<float> Exposure = new InputSlot<float>();

    [Input(Guid = "d09c7a75-d02d-421b-ba0f-17b345f523ec")]
    public readonly InputSlot<Vector4> ChannelWeights = new InputSlot<Vector4>();

    [Input(Guid = "7db67f48-5947-422b-8eb3-33d641cc276e")]
    public readonly InputSlot<Vector2> GainAndBias = new InputSlot<Vector2>();

}