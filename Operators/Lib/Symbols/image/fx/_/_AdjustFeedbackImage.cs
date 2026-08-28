namespace Lib.image.fx.@_;

[Guid("d31a8463-0699-46d7-8e96-9abc6adb997d")]
internal sealed class _AdjustFeedbackImage : Instance<_AdjustFeedbackImage>
{
    [Output(Guid = "ab287fb8-750a-4b46-96f5-8ddddeb46a5f")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "b781c374-5fb7-4f4f-ae6d-78f9bfcd72c6")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "1b29262b-d7b6-4df2-abb1-bf35aa7729a2")]
    public readonly InputSlot<float> LimitDarks = new();

    [Input(Guid = "b255a67e-90d1-45f0-b3a5-45e860b9a2e4")]
    public readonly InputSlot<float> LimitBrights = new();

    [Input(Guid = "a46a797a-9e6e-452f-bc5e-82f8ac2a6fc0")]
    public readonly InputSlot<float> ShiftBrightness = new();

    [Input(Guid = "87cc06e0-7475-40b5-9e45-44bf1334891c")]
    public readonly InputSlot<float> ShiftHue = new();

    [Input(Guid = "77666ad2-1505-4474-aa79-a66d8f2a0eba")]
    public readonly InputSlot<float> ShiftSaturation = new();

    [Input(Guid = "c089eef2-2c29-481e-9777-9917a34345ad")]
    public readonly InputSlot<float> AmplifyEdges = new();

    [Input(Guid = "76b8422f-e0e3-4a6b-922d-0e6f1fc44658")]
    public readonly InputSlot<float> SampleRadius = new();
}