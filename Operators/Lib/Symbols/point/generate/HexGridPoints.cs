namespace Lib.point.generate;

[Guid("a71dc141-2b99-4e63-887c-6408b0f0b83c")]
internal sealed class HexGridPoints : Instance<HexGridPoints>
                            ,ITransformable
{

    [Output(Guid = "0f66a7b7-6225-44fd-9f20-37778545c112")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

        
    public HexGridPoints()
    {
        OutBuffer.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }
        
    [Input(Guid = "4ec062c1-c29f-44a7-8ad3-21711eb24b93")]
    public readonly InputSlot<int> CountX = new();

    [Input(Guid = "476ea0d1-9f6a-4f8a-b6d2-f91781fcde15")]
    public readonly InputSlot<int> CountY = new();

    [Input(Guid = "88a8fc1f-6927-4128-9241-b05b9d32dfe3")]
    public readonly InputSlot<int> CountZ = new();

    [Input(Guid = "9e3f2a7a-5b5f-445f-a67a-6712334dddd6")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "f215e361-15cc-48d5-a90e-f70651587a4f")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "de8b3eee-09ae-45e6-8d94-a52df945118d")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "bf1afcc6-0966-4691-b8d0-664fab364023")]
    public readonly InputSlot<float> W = new();

    [Input(Guid = "b47cca22-d924-4ae9-a8b4-69b9ee8fe822")]
    public readonly InputSlot<Vector3> OrientationAxis = new();

    [Input(Guid = "3aae6a3e-b704-4fb9-b1be-2ff775fa06e8")]
    public readonly InputSlot<float> OrientationAngle = new();

    [Input(Guid = "72081fa8-8e27-43c9-a2f8-e0e595baf500")]
    public readonly InputSlot<Vector3> Pivot = new();

    [Input(Guid = "05821fe1-4a10-4442-8560-036534164002")]
    public readonly InputSlot<int> SizeMode = new();

    private enum SizeModes
    {
        Cell,
        Bounds,
    }
}