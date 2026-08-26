namespace Lib.mesh.modify;

[Guid("6184ac2d-c17d-47de-a314-15e1c670f969")]
internal sealed class SelectVertices : Instance<SelectVertices>, ITransformable
{

    [Output(Guid = "b8cb2689-25bf-46c4-8454-77cb39a1f8cb")]
    public readonly TransformCallbackSlot<MeshBuffers> Result = new();
        
        
    public SelectVertices()
    {
        Result.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => Rotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
        
    [Input(Guid = "f1350cb5-d7c5-43ad-9e0c-4b0aa7a8c0e4")]
    public readonly InputSlot<MeshBuffers> InputMesh = new();
        
    [Input(Guid = "a9807f1e-bd65-4006-a42e-a1d8f67387f9", MappedType = typeof(Shapes))]
    public readonly InputSlot<int> VolumeShape = new(); 
        

    [Input(Guid = "9fcdd319-5120-402b-ac8e-07aa711bdaf5")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "a18f9440-ef80-4d7e-bf69-9e05f5825979")]
    public readonly InputSlot<Vector3> Stretch = new();

    [Input(Guid = "223cfd01-d3e3-402c-85af-1f493ea5f201")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "6a8b0948-37ef-4013-803e-5288cbb0163a")]
    public readonly InputSlot<Vector3> Rotate = new();

    [Input(Guid = "b3230397-8052-4bd6-8ecb-747f62c1bb3d")]
    public readonly InputSlot<float> FallOff = new();
        
    [Input(Guid = "ca4fe7d8-5e78-493b-a209-3ee6be0d0a90", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "a7ed94cf-5038-4ad4-90b2-db6cb2cb142a")]
    public readonly InputSlot<bool> ClampResult = new();

    [Input(Guid = "c1223666-d19a-4fa5-96c6-3582e0a685d0")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "b0942a49-f562-4c46-9a10-3b4de8a3c5b2")]
    public readonly InputSlot<float> Phase = new();

    [Input(Guid = "6c434ee8-f6e9-49cb-af39-878e4b8e490a")]
    public readonly InputSlot<float> Threshold = new();


        
    private enum Shapes
    {
        Sphere,
        Box,
        Plane,
        Zebra,
        Noise,
    }
        
    private enum Modes
    {
        Override,
        Add,
        Sub,
        Multiply,
        Invert,
    }
}