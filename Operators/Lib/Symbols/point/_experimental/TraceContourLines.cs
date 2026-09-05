namespace Lib.point._experimental;

[Guid("46749ae5-ef10-43e7-a712-5cbd7a1d4398")]
internal sealed class TraceContourLines : Instance<TraceContourLines>
                                ,ITransformable
{
    [Output(Guid = "5c2dc61d-012f-478a-ae4d-583ef4696e2d")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public TraceContourLines()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => TextureStretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "4ed5dad4-e9f6-49a2-8aa6-6062b4202012")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "b4e3f2c7-0996-4832-abcc-f0e89e5c56c2")]
    public readonly InputSlot<float> SampleRadius = new();

    [Input(Guid = "e0521d6a-07f2-4b61-9701-5a1f78e732a4")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "818ebbce-9174-4fd1-800e-be816870311e")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "709050be-0873-4a37-96fb-c8fa98780c6f")]
    public readonly InputSlot<Vector2> TextureStretch = new();

    [Input(Guid = "07e800ec-b9cb-45f6-954f-f242b95e9a87")]
    public readonly InputSlot<Vector3> TextureRotate = new();

    [Input(Guid = "0529bdf2-47bf-40bf-8ad9-d925545257e9")]
    public readonly InputSlot<TextureAddressMode> TextureMode = new();

    [Input(Guid = "1cab22cf-494c-4e5c-b968-8e876a28e2e4")]
    public readonly InputSlot<float> TextureScale = new();

    [Input(Guid = "de7eb925-58d9-4300-a0d1-90f7c41722b2")]
    public readonly InputSlot<float> Smoothness = new();

    [Input(Guid = "27a6b0c8-f0e5-4bf2-bb2d-685062d0192d")]
    public readonly InputSlot<float> Curvature = new();
        
    [Input(Guid = "1e5605dd-3dc4-4f50-833a-ffc61d83fca3")]
    public readonly InputSlot<float> StepSize = new();

    [Input(Guid = "3715eeb1-59b5-448f-9bf8-91dc23f71b1d")]
    public readonly InputSlot<int> StepCount = new();



    [Input(Guid = "7da293c9-8c0a-4b6c-98f6-afdda2c16062")]
    public readonly InputSlot<int> ZoneLevels = new();
        
    [Input(Guid = "fbb64646-51d7-4471-8bcd-b549a8413bf4")]
    public readonly InputSlot<float> ZoneWidth = new();

    [Input(Guid = "14034d09-5733-4261-8316-f99fd1978247")]
    public readonly InputSlot<float> ZoneCenter = new();
        

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