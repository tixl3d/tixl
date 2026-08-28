using T3.Core.Utils;

namespace Lib.render._dx11.fxsetup;

[Guid("2b20afce-2b54-4bcc-ba0e-e456a0d92833")]
internal sealed class _ImageFxShaderSetup2 : Instance<_ImageFxShaderSetup2>
{
    [Output(Guid = "36e01dc9-0680-4af5-9329-0ac6a5f78f8c")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "36abde68-0d79-4fb6-b8ce-ac8a27b66850")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "16dce9bc-02d9-48f8-b07e-94a9af48d27b")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "8e9b8826-5870-4db6-93c2-9fb469cfeec9")]
    public readonly MultiInputSlot<float> Params = new();

    [Input(Guid = "c10f16e8-9e59-4b83-91b1-c2c7ad37d52a")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "c80d3700-ceb6-43d0-9ffd-f4854e67a41d")]
    public readonly InputSlot<TextureAddressMode> Wrap = new();

    [Input(Guid = "432c1a23-bf28-4595-923f-6c5a442fa90a")]
    public readonly InputSlot<Format> OutputFormat = new();

    [Input(Guid = "499e7ff0-3b6a-414b-be19-03c615931ab8")]
    public readonly InputSlot<Vector4> BufferColor = new();

    [Input(Guid = "3ca67d25-72ec-487b-91b3-ece912b76462")]
    public readonly InputSlot<bool> GenerateMipmaps = new();

    [Input(Guid = "9911dc7e-cd3b-4c58-a5d0-2e9abc8e528a", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

        [Input(Guid = "3aab7e93-2629-4c6a-b647-cfbd9d5089b2")]
        public readonly InputSlot<SharpDX.Direct3D11.Filter> Filter = new InputSlot<SharpDX.Direct3D11.Filter>();

}