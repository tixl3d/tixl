namespace Lib.mesh.modify;

[Guid("52d0df91-7464-4fff-9173-72c0ee29fced")]
internal sealed class CollapseVertices : Instance<CollapseVertices>
                               ,ITransformable
{

    [Output(Guid = "370afe95-3672-4aa9-b6c9-363e2ea02cc6")]
    public readonly TransformCallbackSlot<MeshBuffers> Result = new();
        
        
    public CollapseVertices()
    {
        Result.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => Rotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "0ab0a287-6fa5-46d3-b427-fea8acac0068")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

    [Input(Guid = "7e0de81f-7858-4955-96f4-27a67503542e")]
    public readonly InputSlot<int> StepCount = new InputSlot<int>();

    [Input(Guid = "82d51310-9d0d-47ff-b5b2-f6f4bdd1ff25")]
    public readonly InputSlot<float> SmoothSteps = new InputSlot<float>();

    [Input(Guid = "afaf4e27-a3f0-4392-9444-0535ebf4ae10")]
    public readonly InputSlot<float> GridSize = new InputSlot<float>();

    [Input(Guid = "0842b182-57da-44f4-97bc-60f0e771015e")]
    public readonly InputSlot<Vector3> GridOffset = new InputSlot<Vector3>();

    [Input(Guid = "350aa9d5-90cd-44ac-8740-9864727e9e4f", MappedType = typeof(Shapes))]
    public readonly InputSlot<int> VolumeShape = new InputSlot<int>();

    [Input(Guid = "6b27418e-f0e4-4ff5-8b1c-d5979f837e16")]
    public readonly InputSlot<Vector3> Center = new InputSlot<Vector3>();

    [Input(Guid = "a4436ed0-0013-4ed9-b789-9f96a03e7571")]
    public readonly InputSlot<Vector3> Stretch = new InputSlot<Vector3>();

    [Input(Guid = "052ddd9e-e4d4-4c68-b31c-1adf82696dda")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "df1ae7cb-f21b-4825-b3fa-074fcd23ff4e")]
    public readonly InputSlot<Vector3> Rotate = new InputSlot<Vector3>();

    [Input(Guid = "e781d07d-55ba-4227-9747-d0a0b60b47e8")]
    public readonly InputSlot<float> FallOff = new InputSlot<float>();

    [Input(Guid = "9586a163-0e1b-4229-b368-1fdb06938b2b")]
    public readonly InputSlot<float> Phase = new InputSlot<float>();

    [Input(Guid = "963b800e-32d9-48dc-8f14-6270606056be")]
    public readonly InputSlot<float> Extend = new InputSlot<float>();

    [Input(Guid = "3a0c49ff-832a-45d0-bc3d-8b11a75654bc")]
    public readonly InputSlot<MeshBuffers> InputMesh = new InputSlot<MeshBuffers>();


        
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