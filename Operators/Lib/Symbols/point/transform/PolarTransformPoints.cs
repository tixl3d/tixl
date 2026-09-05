namespace Lib.point.transform;

[Guid("3d255f3e-d2e2-4f61-a03d-5af7043fabfc")]
internal sealed class PolarTransformPoints : Instance<PolarTransformPoints>
                                   ,ITransformable
{
    [Output(Guid = "62a9bc7b-4678-409a-8e26-7f6377b72cb0")]
    public readonly TransformCallbackSlot<BufferWithViews> Output = new();

    public PolarTransformPoints()
    {
        Output.TransformableOp = this;
    }        
    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "83d00528-423a-43f9-8750-97d7a4909c49")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "eb1ba2fe-1bc5-41c0-8acb-875fb3faa8ae")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "c7f7e8d2-8694-4eab-9693-c3e6c1ec95e8")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "433a0c6d-fd59-49d6-8476-601a316f0b88")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "f929e486-a49f-445b-962f-e0f3fc7d52cc")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "8fa1db66-53aa-4737-983b-91deda39bb65", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();
        
    private enum Modes
    {
        CartesianToCylindrical,
        CartesianToSpherical,
    }
        
        
        
        
        
}