namespace Lib.mesh.modify;

[Guid("81d27ec1-2d02-4352-98d2-f2bfb0d726bc")]
internal sealed class PuppetMesh :Instance<PuppetMesh>,ITransformable
{
    [Output(Guid = "35ce06c8-21ff-497f-817c-797a319a89c4")]
    public readonly TransformCallbackSlot<MeshBuffers> Result = new();
        
    public PuppetMesh()
    {
        Result.TransformableOp = this;
    }        
        
    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "e0aa5992-1893-4126-b6b5-fc90c010e669")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "34ec39a9-7009-49b1-9ca4-ab26b901c06d")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "aa70bc7c-15b6-497b-90d0-6b11de7b8ca0")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "08468acd-f7c7-493b-973c-5814f1b3516e")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "b26a2a9d-36ea-43fd-9af8-b6b5fd791096")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "46762c06-6bcc-4fda-8010-73a6881e36be")]
    public readonly InputSlot<bool> UseVertexSelection = new();

    [Input(Guid = "f0364e79-ca6a-4a96-85c7-6eb88bf27654")]
    public readonly InputSlot<Vector3> Pivot = new();

        [Input(Guid = "2f4d18c2-2a68-41f0-8e7d-52de359f9d85")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> AnchorPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "039daa17-44bb-456d-9f29-b86abbf308fe")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> AnchorPointsCurrent = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "a96c3b81-609a-498a-96e7-ac829ba29c72")]
        public readonly InputSlot<bool> ShowWeight = new InputSlot<bool>();

        [Input(Guid = "752ca6e8-e2d6-4439-afa1-7f14d644d3a6")]
        public readonly InputSlot<float> WeightStrength = new InputSlot<float>();
        
        
        
        
        
        
        
        
        
}