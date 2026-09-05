namespace Lib.point.sim;

[Guid("e85f0fcb-303e-4d21-b592-ad4578286336")]
internal sealed class SamplePointSimAttributes : Instance<SamplePointSimAttributes>
                                       ,ITransformable
{
    [Output(Guid = "03f3201d-a921-4853-9029-7901cf0cffea")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public SamplePointSimAttributes()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => TextureScale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
        
    [Input(Guid = "d153e6a4-1ec9-4164-b39a-032e142ba7aa")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "106471ad-4c27-472b-b5c9-4534ceb77b7e", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Brightness = new();

    [Input(Guid = "9f5df1ff-d282-4a7a-88cc-b14867c15108")]
    public readonly InputSlot<float> BrightnessFactor = new();

    [Input(Guid = "a4a39f14-1998-4358-af2d-a7ce2e9a0720")]
    public readonly InputSlot<float> BrightnessOffset = new();

    [Input(Guid = "2b0ad87f-d4f0-4e31-962a-30fb868f73c7", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Red = new();

    [Input(Guid = "58113aa8-323b-4992-b838-5c01929b705c")]
    public readonly InputSlot<float> RedFactor = new();

    [Input(Guid = "51dcd623-b418-4dcd-85d9-02687dcfdec7")]
    public readonly InputSlot<float> RedOffset = new();

    [Input(Guid = "570ab778-913a-4756-be87-81fb0dc5ea3e", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Green = new();

    [Input(Guid = "1af43a7f-1d5f-48d4-a27a-fa22bd307737")]
    public readonly InputSlot<float> GreenFactor = new();

    [Input(Guid = "c691c083-29c5-403c-bc7e-88f25a7f0b8a")]
    public readonly InputSlot<float> GreenOffset = new();

    [Input(Guid = "5cc58801-a9ea-44ab-82b1-fb717cae3adb", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Blue = new();

    [Input(Guid = "8d6ea73f-f968-4d33-89df-9605cb800414")]
    public readonly InputSlot<float> BlueFactor = new();

    [Input(Guid = "4a53aefe-9b4f-4a14-96a5-ddea6dd65e45")]
    public readonly InputSlot<float> BlueOffset = new();

    [Input(Guid = "c868345f-0318-4da7-968f-d859661d5b7e")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "57e60306-a398-432b-ac02-9e6334608264")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "2d2c9755-9bef-465b-8934-5f2d8a0193ca")]
    public readonly InputSlot<Vector2> TextureScale = new();

    [Input(Guid = "5b7a1e63-6e09-448f-ac7e-78e1e8278a90")]
    public readonly InputSlot<Vector3> TextureRotate = new();

    [Input(Guid = "152ee687-aefc-4261-8a3e-a01b3fe4e78f")]
    public readonly InputSlot<TextureAddressMode> TextureMode = new();

    [Input(Guid = "42c3ba03-75c4-4c8e-b953-9f1cb7ed0b02")]
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