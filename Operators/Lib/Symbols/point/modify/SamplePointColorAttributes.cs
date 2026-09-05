using T3.Core.Utils;

namespace Lib.point.modify;

[Guid("056ea55a-91a2-4b55-bcce-e44cc8602623")]
internal sealed class SamplePointColorAttributes : Instance<SamplePointColorAttributes>
                                         ,ITransformable
{
    [Output(Guid = "8a18f42d-6563-4731-bff0-d85210d3fbee")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public SamplePointColorAttributes()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "6073c4b2-6c52-4dca-8ae7-bcf700007971")]
    public readonly InputSlot<BufferWithViews> GPoints = new InputSlot<BufferWithViews>();

    [Input(Guid = "32e576f6-bcee-4762-babf-86aa95930e52")]
    public readonly InputSlot<Texture2D> Texture = new InputSlot<Texture2D>();

    [Input(Guid = "6d244ad5-04d3-47dd-8986-b5bf90ee5b0a")]
    public readonly InputSlot<Vector4> BaseColor = new InputSlot<Vector4>();

    [Input(Guid = "adcb9860-0c16-4859-9af6-6eb3a2197b39", MappedType = typeof(SharedEnums.RgbBlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();

    [Input(Guid = "8416b39e-12fc-4052-9eab-95e8c035ceaa")]
    public readonly InputSlot<Vector3> Center = new InputSlot<Vector3>();

    [Input(Guid = "7ebfbecb-bd25-4a6c-a873-f2bbdf6a9aeb")]
    public readonly InputSlot<Vector2> Stretch = new InputSlot<Vector2>();

    [Input(Guid = "7139213f-fb73-42b6-8b2a-ffb05c2aeaf9")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "66b5e5eb-7743-48c9-979d-48d8cc492456")]
    public readonly InputSlot<Vector3> TextureRotate = new InputSlot<Vector3>();

    [Input(Guid = "9ae109e2-1e13-468c-b91c-6af77eb67d4f")]
    public readonly InputSlot<TextureAddressMode> TextureMode = new InputSlot<TextureAddressMode>();

    [Input(Guid = "e5162ad6-3a6a-4e2f-ae1c-ae365739f51f")]
    public readonly InputSlot<GizmoVisibility> Visibility = new InputSlot<GizmoVisibility>();

}