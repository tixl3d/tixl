namespace Lib.render.basic;

[Guid("fd31d208-12fe-46bf-bfa3-101211f8f497")]
internal sealed class Text : Instance<Text>, ITransformable
{
    public enum HorizontalAligns
    {
        Left,
        Center,
        Right,
    }
        
    public enum VerticalAligns
    {
        Top,
        Middle,
        Bottom,
    }
        
    [Output(Guid = "3f8b20a7-c8b8-45ab-86a1-0efcd927358e", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TransformCallbackSlot<Command> Output = new();

        
    public Text()
    {
        Output.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "f1f1be0e-d5bc-4940-bbc1-88bfa958f0e1")]
    public readonly InputSlot<string> InputText = new InputSlot<string>();

    [Input(Guid = "0e5f05b4-5e8a-4f6d-8cac-03b04649eb67")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "989e6950-fd32-4d0b-97c2-d03264cb2db5")]
    public readonly InputSlot<Vector4> Shadow = new InputSlot<Vector4>();

    [Input(Guid = "de0fed7d-d2af-4424-baf3-81606e26559f")]
    public readonly InputSlot<Vector2> Position = new InputSlot<Vector2>();

    [Input(Guid = "50c9ab21-39f4-4e92-b5a7-ad9e60ae6ec3")]
    public readonly InputSlot<string> FontPath = new InputSlot<string>();

    [Input(Guid = "d89c518c-a862-4f46-865b-0380350b7417")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "835d7f17-9de4-4612-a2f0-01c1346cdf1a")]
    public readonly InputSlot<float> Spacing = new InputSlot<float>();

    [Input(Guid = "eaf9dc37-e70f-4197-895c-b5607456b4a2")]
    public readonly InputSlot<float> LineHeight = new InputSlot<float>();

    [Input(Guid = "ae7f7e83-fa18-44fd-b639-3bd0dbd3ac06", MappedType =  typeof(VerticalAligns))]
    public readonly InputSlot<int> VerticalAlign = new InputSlot<int>();

    [Input(Guid = "82cc31ff-3307-4b6d-be70-16e471c2ffc9", MappedType = typeof(HorizontalAligns))]
    public readonly InputSlot<int> HorizontalAlign = new InputSlot<int>();

    [Input(Guid = "28be4e86-6761-4d07-80bf-abf6f82897e4")]
    public readonly InputSlot<CullMode> CullMode = new InputSlot<CullMode>();

    [Input(Guid = "7a76d5aa-1f44-4238-9333-7c2951becc31")]
    public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "356cbd6c-2e6a-4a3e-992f-943dd420a53f")]
        public readonly InputSlot<int> Sharpness = new InputSlot<int>();
}