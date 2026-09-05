namespace Lib.render.sprite._experimental;

[Guid("4670ff79-f630-478c-8f3f-513e1f2b5913")]
internal sealed class SampleSpriteAttributes : Instance<SampleSpriteAttributes>
                                     ,ITransformable
{
    [Output(Guid = "7d977f4b-8e79-46bf-a2b0-a1bb44cd249d")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public SampleSpriteAttributes()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => TextureScale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "df943375-bba4-4f90-a3e2-5226aaf4069d")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "40da9800-2f7a-46c0-86f6-731b39a10003")]
    public readonly InputSlot<BufferWithViews> Sprites = new();

    [Input(Guid = "1940c248-bafb-4089-80fb-d6d4178af3e9")]
    public readonly InputSlot<int> Brightness = new();

    [Input(Guid = "edca2152-c2f6-49a3-ab75-4c4b52797906")]
    public readonly InputSlot<float> BrightnessFactor = new();

    [Input(Guid = "9ac10467-2d69-43cb-bb61-2afffbaf45eb")]
    public readonly InputSlot<float> BrightnessOffset = new();

    [Input(Guid = "b4848422-91cd-4dab-8be9-b5709233c9ac")]
    public readonly InputSlot<int> Red = new();

    [Input(Guid = "25f604da-2df7-4b51-b7c2-e0ea3f924737")]
    public readonly InputSlot<float> RedFactor = new();

    [Input(Guid = "b50899b3-808c-4e97-85b6-363b6cdbbc21")]
    public readonly InputSlot<float> RedOffset = new();

    [Input(Guid = "bd041fd4-1372-4171-b8a8-eb3940f955e9")]
    public readonly InputSlot<int> Green = new();

    [Input(Guid = "dea3c93d-8c62-4dec-baad-c41c9a955347")]
    public readonly InputSlot<float> GreenFactor = new();

    [Input(Guid = "297c7c06-9e52-4c6b-8bf7-abbc8da9208d")]
    public readonly InputSlot<float> GreenOffset = new();

    [Input(Guid = "c4f741fb-cd3f-49bc-a751-ca474040c351")]
    public readonly InputSlot<int> Blue = new();

    [Input(Guid = "7badd6bd-5999-45d5-9e49-43276f6ac9e9")]
    public readonly InputSlot<float> BlueFactor = new();

    [Input(Guid = "a9c2ac31-ecd8-4802-bc5b-083725a1b1f5")]
    public readonly InputSlot<float> BlueOffset = new();

    [Input(Guid = "cc572e12-8dec-4f92-90a2-766d9299aaad")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "4e2bdf5e-9584-442a-b0f2-91b94955ebd5")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "2286ff57-abdc-43b4-80ac-4acb509ab3b5")]
    public readonly InputSlot<Vector2> TextureScale = new();

    [Input(Guid = "d6dd78d8-55a7-4e1e-8321-b206c7ba668f")]
    public readonly InputSlot<Vector3> TextureRotate = new();

    [Input(Guid = "ef5d0f4a-6ef2-4735-8373-847ba87f598d")]
    public readonly InputSlot<TextureAddressMode> TextureMode = new();

    [Input(Guid = "1081643f-efab-43e3-87c9-58b6953f3338")]
    public readonly InputSlot<GizmoVisibility> Visibility = new();


    private enum Attributes
    {
        NotUsed =0,
        For_X = 1,
        For_Y =2,
        For_Z =3,
        For_W =4,
        Rotate_X =5,
        Rotate_Y =6 ,
        Rotate_Z =7,
    }
}