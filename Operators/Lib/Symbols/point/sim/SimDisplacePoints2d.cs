namespace Lib.point.sim;

[Guid("9d6cc9a3-980f-46ae-bf79-02fc1f49c480")]
internal sealed class SimDisplacePoints2d : Instance<SimDisplacePoints2d>
                                  ,ITransformable
{
    [Output(Guid = "314a5657-ab4e-4dac-8eeb-a6bb3122af45")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public SimDisplacePoints2d()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => TextureScale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "fd89196c-22ee-4ac3-813f-d1ea9306eda3")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "d7a052ba-f4ec-4607-a028-b92b3d497209")]
    public readonly InputSlot<float> DisplaceAmount = new();

    [Input(Guid = "8d0d5411-2a15-4cfe-ab5e-2c7b5ac9ef33")]
    public readonly InputSlot<float> DisplaceOffset = new();

    [Input(Guid = "937950ba-7992-4be1-900d-459ea91be791")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "6ae9a21e-3aea-410b-b740-28390a4b4715")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "e2add306-b57c-466d-af22-580aa333a697")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "dc25b3a9-c3fc-4a45-a926-d2033019f12c")]
    public readonly InputSlot<Vector2> TextureScale = new();

    [Input(Guid = "8ae1b366-72d4-42fd-bfa9-df70721a0671")]
    public readonly InputSlot<Vector3> TextureRotate = new();

    [Input(Guid = "9f9c0795-5d84-4687-9d4b-49c0776ec983")]
    public readonly InputSlot<TextureAddressMode> TextureMode = new();

    [Input(Guid = "36892b67-0f27-4fdd-b847-42de81e612c8")]
    public readonly InputSlot<float> SampleRadius = new();


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