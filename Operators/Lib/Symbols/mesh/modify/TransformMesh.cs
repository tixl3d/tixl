namespace Lib.mesh.modify;

[Guid("026e6917-6e6f-4ee3-b2d4-58f4f1de74c9")]
internal sealed class TransformMesh : Instance<TransformMesh>, ITransformable
{
    [Output(Guid = "9ff1bfed-4554-4c55-9557-8b318ac47afe")]
    public readonly TransformCallbackSlot<MeshBuffers> Result = new();
        
    public TransformMesh()
    {
        Result.TransformableOp = this;
    }        
        
    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "c2c9afc7-3474-40c3-be82-b9f48c92a2c5")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "da607ebd-6fec-4ae8-bf91-b70dcb794557")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "1168094f-1eee-4ed7-95e2-9459e6171e08")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "f37c11a5-b210-4e83-8ebd-64ea49ee9b96")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "86791d0a-97c3-413a-89d9-aa2ddd40ce4a")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "71531810-78ab-449e-bb13-bfe5fe3d2a69")]
    public readonly InputSlot<bool> UseVertexSelection = new();

    [Input(Guid = "ccd89dd2-1baa-4a0c-8ec3-5a0e77551379")]
    public readonly InputSlot<Vector3> Pivot = new();
        
        
        
        
        
        
        
        
        
}