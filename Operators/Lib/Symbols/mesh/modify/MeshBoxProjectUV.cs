namespace Lib.mesh.modify;

[Guid("19c9fafe-12c2-4c1d-a154-94af400a6140")]
internal sealed class MeshBoxProjectUV : Instance<MeshBoxProjectUV>
                            ,ITransformable
{
    [Output(Guid = "7FC10C41-D8D7-4FA2-B82B-BBB994D9A1AA")]
    public readonly TransformCallbackSlot<MeshBuffers> OutBuffer = new();

    public MeshBoxProjectUV()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Translate;
    IInputSlot ITransformable.RotationInput => Rotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "537cf376-1151-4802-b64c-2d3e918536e5")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "faa8d4e0-8420-4907-b677-ebb3ed6ccc15")]
    public readonly InputSlot<Vector3> Translate = new();

    [Input(Guid = "749851ce-e149-441d-be7c-8f66c1aa8869")]
    public readonly InputSlot<Vector3> Rotate = new();

    [Input(Guid = "4c043452-332d-473c-9d1a-0bb56359d345")]
    public readonly InputSlot<Vector3> Stretch = new();
        

    [Input(Guid = "2c9ff254-39d8-41b3-b9e9-2a9042a73dbd")]
    public readonly InputSlot<float> Scale = new();

        [Input(Guid = "97cf607a-b83f-472d-ade0-767f8a79cdd9")]
        public readonly InputSlot<bool> ToTexCoord2 = new InputSlot<bool>();
}