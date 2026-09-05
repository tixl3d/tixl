namespace Lib.image.fx.@_;

[Guid("a2567844-3314-48de-bda7-7904b5546535")]
internal sealed class _multiImageFxSetup : Instance<_multiImageFxSetup>
{
    [Output(Guid = "b6bd9c40-1695-46d0-925e-dbaa7882f0ff")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "7f14d0e3-1159-434d-b038-74644948937c")]
    public readonly InputSlot<string> ShaderPath = new();

    [Input(Guid = "fc069ee6-7d18-4856-bcf3-1e7c9b8fd4d8")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "c3da7928-5c0c-4478-9412-fd4b68a094d5")]
    public readonly InputSlot<Texture2D> ImageB = new();

    [Input(Guid = "bcc7fb78-1ac3-46f7-be46-885233420e80")]
    public readonly MultiInputSlot<float> FloatParams = new();

    [Input(Guid = "6aa3113a-7f53-4dc6-a79e-2d818c5c5c25")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "a5cb5bda-0fb2-4863-bd8d-9ac09135fc30")]
    public readonly InputSlot<TextureAddressMode> WrapMode = new();

    [Input(Guid = "fe1695b3-54db-4b53-8987-3216bcf59da9")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "440e471b-9f67-435c-9e21-304cd57728aa")]
    public readonly InputSlot<bool> GenerateMips = new();

        [Input(Guid = "46d12c19-3db7-4cb3-b7ba-a4013d0a65f9")]
        public readonly MultiInputSlot<int> IntParams = new MultiInputSlot<int>();
}