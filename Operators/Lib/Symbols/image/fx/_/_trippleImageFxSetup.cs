namespace Lib.image.fx.@_;

[Guid("5b999887-19df-4e91-9f58-1df2d8f1440b")]
internal sealed class _trippleImageFxSetup : Instance<_trippleImageFxSetup>
{
    [Output(Guid = "86db735f-56fb-41b5-af15-5f55411d3ca7")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "58dd103d-4172-4cea-9c78-c9f6db9be41e")]
    public readonly InputSlot<string> ShaderPath = new();

    [Input(Guid = "9f6dab55-54bf-4c21-93d1-b2bb6beb8c5c")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "85d0fe6a-145e-4b17-ad00-62ad7afe58e4")]
    public readonly InputSlot<Texture2D> ImageB = new();

    [Input(Guid = "4e6a74d8-203a-4621-b167-85e109da204f")]
    public readonly InputSlot<Texture2D> ImageC = new();

    [Input(Guid = "39c7dd84-7418-49e6-850e-6064db28660c")]
    public readonly MultiInputSlot<float> FloatParams = new();

    [Input(Guid = "37d55b2b-c2ca-4d7f-97b1-d9d33efc2658")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "1f30e247-bcf8-43d0-b91f-7d87bd4f6d11")]
    public readonly InputSlot<TextureAddressMode> WrapMode = new();

    [Input(Guid = "38509eb8-5d3f-4f27-a8fa-5752aa86f1a5")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "9d3927cf-4062-40cf-8643-c9a64adcc9cb")]
    public readonly InputSlot<bool> BlendEnabled = new();

    [Input(Guid = "9a7c1431-a33d-4e78-b36d-e54ec5521d3e")]
    public readonly InputSlot<bool> GenerateMips = new();
}