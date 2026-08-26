namespace Lib.image.fx.@_;

[Guid("cc34a183-3978-4b6b-8ef1-dd8102410816")]
internal sealed class _multiImageFxSetupStatic : Instance<_multiImageFxSetupStatic>
{
    [Output(Guid = "76b6c677-12db-4404-aff7-ee3391d2d831")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "f6269be3-3331-43a6-91ec-138b47199f3e")]
    public readonly InputSlot<string> ShaderPath = new();

    [Input(Guid = "55126bff-8c94-415d-96dd-3c16e216e663")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "0bb90f8d-88c9-4a99-b44f-f284b505c65b")]
    public readonly InputSlot<Texture2D> ImageB = new();

    [Input(Guid = "2929c4c9-6d6a-47b7-b80e-d7a1f90b6945")]
    public readonly MultiInputSlot<float> FloatParams = new();

    [Input(Guid = "9851a909-a9fd-4feb-a8bd-48846cea8461")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "6022a4b0-c6fc-49a3-811a-179dcceb8b44")]
    public readonly InputSlot<TextureAddressMode> WrapMode = new();

    [Input(Guid = "e31b78eb-940b-41df-93fa-d0c1c9f864f4")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "35f3b28b-2210-4f27-813a-5c857940d09c")]
    public readonly InputSlot<Format> TextureFormat = new InputSlot<Format>();

        [Input(Guid = "333c2bba-48bc-4901-b94d-5a7c46c2b0bd")]
        public readonly MultiInputSlot<int> IntParameters = new MultiInputSlot<int>();

        [Input(Guid = "9997e726-3951-4822-af7e-ffbf26164723")]
        public readonly InputSlot<SharpDX.Direct3D11.Filter> TextureFilter = new InputSlot<SharpDX.Direct3D11.Filter>();
}