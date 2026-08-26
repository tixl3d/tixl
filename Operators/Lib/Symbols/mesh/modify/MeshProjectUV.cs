namespace Lib.mesh.modify;

[Guid("97ffb173-f4cc-4143-a479-80cf3465cc7e")]
internal sealed class MeshProjectUV : Instance<MeshProjectUV>
                            ,ITransformable
{
    [Output(Guid = "84C6619E-4264-4D16-B870-5413D986F08A")]
    public readonly TransformCallbackSlot<MeshBuffers> OutBuffer = new();

    public MeshProjectUV()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Translate;
    IInputSlot ITransformable.RotationInput => Rotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "10bc1ef8-e036-4da0-9bc8-65da0ddff7f0")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "8a991e5f-361c-4ab9-9660-bdf759c87594")]
    public readonly InputSlot<Vector3> Translate = new();

    [Input(Guid = "4da35d2e-4e12-44ee-8b06-6dfe54be104f")]
    public readonly InputSlot<Vector3> Rotate = new();

    [Input(Guid = "432c3388-907b-4b7f-8c6e-73a5100bc43a")]
    public readonly InputSlot<Vector3> Stretch = new();
        

    [Input(Guid = "2ce29895-0bfd-4b73-ad57-50aca7fd1a96")]
    public readonly InputSlot<float> Scale = new();

        [Input(Guid = "02c87571-ebaa-4cf9-bc6d-335911b50d07")]
        public readonly InputSlot<bool> ToTexCoord2 = new InputSlot<bool>();
}