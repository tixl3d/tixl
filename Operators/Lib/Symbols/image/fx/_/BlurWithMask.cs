namespace Lib.image.fx.@_;

[Guid("d40966c3-2369-40f2-8202-e5c8ab6d9cc0")]
internal sealed class BlurWithMask : Instance<BlurWithMask>
{
    [Output(Guid = "8d199a8d-b02e-4fa2-8f7d-b156e4302fe3")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "29f6bc05-de55-4336-a275-f06b835c66f8")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "4837051f-033c-4e9e-9d1c-0fe85c1467cb")]
    public readonly InputSlot<Texture2D> Mask = new InputSlot<Texture2D>();

    [Input(Guid = "fa5bb047-7466-4d68-9977-7a86815ca0f2")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "25091e3d-36ef-4892-965c-b7d3c983da22")]
    public readonly InputSlot<float> Samples = new InputSlot<float>();

    [Input(Guid = "0623d858-8986-4058-b209-28b0649f1441")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "1bfb5c46-a1dd-41fe-aa6b-96e3d602bc82")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "62971a40-08ef-414c-8bb5-31ee050551ea")]
    public readonly InputSlot<float> AddOriginal = new InputSlot<float>();

    [Input(Guid = "731eaf1c-aef0-4310-a470-62c0dfeb310a")]
    public readonly InputSlot<float> ApplyMaskToAlpha = new InputSlot<float>();

    [Input(Guid = "d89ac5e9-9bdd-48f6-8118-3bbe04e3988d")]
    public readonly InputSlot<float> MaskContrast = new InputSlot<float>();

    [Input(Guid = "684761a9-3bdf-4ef3-8d3e-191332609ecf")]
    public readonly InputSlot<float> MaskOffset = new InputSlot<float>();

}