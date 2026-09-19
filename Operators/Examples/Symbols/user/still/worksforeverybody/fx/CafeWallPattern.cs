namespace Examples.user.still.worksforeverybody.fx;

[Guid("6854e04f-8b60-41b2-a369-ca0b715c4df3")]
internal sealed class CafeWallPattern : Instance<CafeWallPattern>
{
    [Output(Guid = "cdf70feb-2cb0-44cd-bf4b-38c7af917029")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "2ac6c8b8-ebf5-4653-b390-da008c4be0e6")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "39ae4389-b20d-42b1-b0a4-a9359e3e2f2b")]
    public readonly InputSlot<System.Numerics.Vector4> Fill = new();

    [Input(Guid = "635cba46-9c44-4cf9-a03c-9647e13fea83")]
    public readonly InputSlot<System.Numerics.Vector4> Background = new();

    [Input(Guid = "a5e7133e-5ba6-4f01-9a2f-a8fb591aae54")]
    public readonly InputSlot<System.Numerics.Vector4> EdgeColor = new();

    [Input(Guid = "78082902-ffb0-47c2-a611-6c1ca8d2a599")]
    public readonly InputSlot<System.Numerics.Vector2> Stretch = new();

    [Input(Guid = "6c55b1a0-8092-42ec-b143-69d782298e7a")]
    public readonly InputSlot<System.Numerics.Vector2> Offset = new();

    [Input(Guid = "f334560f-d745-4a1e-9df6-e0acba043d31")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "6415887f-bae9-4e2c-a5d1-cc09f9d6c6bd")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "c98d3ff9-f4d7-482e-90bc-fe1b32382766")]
    public readonly InputSlot<float> Feather = new();

    [Input(Guid = "c1c35717-7ea6-4507-bb7f-bfc827e0446e")]
    public readonly InputSlot<float> Ratio = new();

    [Input(Guid = "e023f382-014e-4f37-ab9e-5914a3924071")]
    public readonly InputSlot<float> EdgeWidth = new();

    [Input(Guid = "38a04ab5-b0d7-4c69-9647-fc86534892af")]
    public readonly InputSlot<float> RowSwift = new();

    [Input(Guid = "0199068d-c826-4b08-9987-8e480ce064a7")]
    public readonly InputSlot<float> RAffects_Ratio = new();

    [Input(Guid = "6a34fae1-0c39-4a3a-bc72-1f8fde957b27")]
    public readonly InputSlot<float> GAffects_EdgeWidth = new();

    [Input(Guid = "0480f95d-3ab6-4d53-8452-bd79a5438d71")]
    public readonly InputSlot<float> BAffects_RowShift = new();

    [Input(Guid = "d3827fcf-ccee-471e-8cbd-1ffd2155be40")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "9bc1afbb-d9e5-4bc8-afa9-2cbba0215a5a")]
    public readonly InputSlot<float> AmplifyIllusion = new();
}