namespace Lib.image.generate.basic;

[Guid("1a411be2-1757-4019-8ce2-e29f808ed839")]
internal sealed class CheckerBoard : Instance<CheckerBoard>
{
    [Output(Guid = "9dd9dbeb-b506-4d10-97b7-34feaab91f07")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "1192cfbe-585f-45f0-a37b-5fe78ca32d7b")]
    public readonly InputSlot<Vector4> ColorA = new InputSlot<Vector4>();

    [Input(Guid = "b08aba90-f33f-4402-bb7b-bcfc4bb624ce")]
    public readonly InputSlot<Vector4> ColorB = new InputSlot<Vector4>();

    [Input(Guid = "70c4ecc7-72a2-42ee-8546-cbff2c08aa27")]
    public readonly InputSlot<Vector2> Stretch = new InputSlot<Vector2>();

    [Input(Guid = "e367bd72-c37c-4a7f-8441-698161ba75f8")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "48d5cd1d-3ca1-4075-a55e-21cec39b4525")]
    public readonly InputSlot<bool> UseAspectRatio = new InputSlot<bool>();

    [Input(Guid = "cb36f66b-99d7-4468-adbf-86e168a76ade")]
    public readonly InputSlot<Vector2> Offset = new InputSlot<Vector2>();

    [Input(Guid = "63ebb6c9-e8a5-43e4-97a4-3a34ad585474")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();

        [Input(Guid = "6f38fabe-f2b0-48b1-92fb-ea36b4fa20d8")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();
}