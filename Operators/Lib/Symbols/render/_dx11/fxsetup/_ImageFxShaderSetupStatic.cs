namespace Lib.render._dx11.fxsetup;

[Guid("bd0b9c5b-c611-42d0-8200-31af9661f189")]
public sealed class _ImageFxShaderSetupStatic : Instance<_ImageFxShaderSetupStatic>
{
    [Output(Guid = "d49f10dc-6617-4541-96d3-b58b0266b631")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "b55312c4-6441-473f-a107-df50de60c72e")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "1e4e274b-60b2-4fe8-b275-ebef80d520a7")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "4ef6f204-1894-4b0a-bb2d-8b5ecbad4040")]
    public readonly MultiInputSlot<float> Params = new();

    [Input(Guid = "9695b557-433c-474b-bf34-219cbc134bee")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "3fe1b650-ce34-4155-9b61-0425e39f7690")]
    public readonly InputSlot<TextureAddressMode> Wrap = new();

    [Input(Guid = "ff7cb999-aa3a-4e11-b9c8-d027bdb55ff6")]
    public readonly InputSlot<Format> OutputFormat = new();

    [Input(Guid = "6cf68692-43a5-4a93-873c-99aa0d2dde93")]
    public readonly InputSlot<Vector4> BufferColor = new();

    [Input(Guid = "480d6c19-a33c-48b3-b9fa-aaf8bd31e6d9")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "2624d563-bad7-4fcb-a201-0ee0b9b93afa")]
    public readonly InputSlot<Filter> Filter = new();

        [Input(Guid = "86fe6e64-5301-4eb7-b91d-581d2f775db2")]
        public readonly MultiInputSlot<int> IntParams = new MultiInputSlot<int>();

}