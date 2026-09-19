namespace Examples.templates;

[Guid("fdd58452-ecb4-458d-9f5b-9bce356d5125")]
internal sealed class _ImgFxShaderTemplate : Instance<_ImgFxShaderTemplate>
{
    [Output(Guid = "46381071-48e7-4ae7-a5c2-63bcd0fba47b")]
    public readonly Slot<Texture2D> TextureOutput = new();


    [Input(Guid = "bf5f239e-8f6b-4fea-86d2-95e3add1a28c")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "4583d292-de66-4bff-abe6-5c2f5920b18b")]
    public readonly InputSlot<float> SampleRadius = new();

    [Input(Guid = "688b7214-63bb-4c9f-ac77-afaae14759ab")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "aa034c28-5a92-44fa-ade5-30c27bbe7abb")]
    public readonly InputSlot<float> Contrast = new();

    [Input(Guid = "5927064b-5119-4b92-b90f-e30034761c03")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();

    [Input(Guid = "c9575f3a-095d-40d2-98c6-7f3396014400")]
    public readonly InputSlot<float> MixOriginal = new();
}