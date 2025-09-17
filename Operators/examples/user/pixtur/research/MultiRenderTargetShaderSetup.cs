namespace Examples.user.pixtur.research;

[Guid("15ec2cc2-8c9e-41b7-8b55-0f39532a0882")]
public class MultiRenderTargetShaderSetup : Instance<MultiRenderTargetShaderSetup>
{
    [Output(Guid = "1cd18000-1e59-4262-929a-61ab9a26afcf")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Output(Guid = "53b4e530-878f-4d70-9283-576329ea194b")]
    public readonly Slot<Texture2D> Output2 = new();

    [Output(Guid = "b1c54e99-2bb7-4acc-9a23-c99f3ad44614")]
    public readonly Slot<Texture2D> Output3 = new();

    [Input(Guid = "4ce89122-f988-46b6-94bb-6c94bc386ec3")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "5d3770ad-b90d-4086-8391-3c7a3ad4b750")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "49011f24-e689-46ea-abfe-044fb88023df")]
    public readonly MultiInputSlot<float> Params = new();

    [Input(Guid = "adb421e9-e2df-4ac6-82b2-b3c00db519c1")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "29df8ec3-71df-42a0-b9f5-a4864fe0d246")]
    public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> Wrap = new();

    [Input(Guid = "5815e180-7189-4b12-af14-80f04302d4e8")]
    public readonly InputSlot<SharpDX.DXGI.Format> OutputFormat = new();

    [Input(Guid = "553309fd-04b3-40a4-b97c-b7898e67f9f4")]
    public readonly InputSlot<System.Numerics.Vector4> BufferColor = new();

    [Input(Guid = "8b7f72fa-9c85-44c5-ae2e-d5491e08b610")]
    public readonly InputSlot<bool> GenerateMipmaps = new();

    [Input(Guid = "1c70d8df-7682-46ff-bf43-297dad84b91f")]
    public readonly InputSlot<int> BlendMode = new();

}