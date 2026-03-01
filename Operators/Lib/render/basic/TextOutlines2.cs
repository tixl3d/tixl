namespace Lib.render.basic;

[Guid("84e9333f-38d2-422f-abf5-bf3929f01c7e")]
internal sealed class TextOutlines2 : Instance<TextOutlines2>
                            ,ITransformable
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
        
    [Output(Guid = "58150633-cb65-416c-aad3-3496e930dfd0", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TransformCallbackSlot<Command> Output = new();

        
    public TextOutlines2()
    {
        Output.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "c4b53d8f-1357-47d4-aa73-ca6d07543994")]
    public readonly InputSlot<string> InputText = new InputSlot<string>();

    [Input(Guid = "4da54683-460e-4af6-a729-5c631e22285a")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();

    [Input(Guid = "9c2089a2-266d-4022-b23d-4c67a8a19788")]
    public readonly InputSlot<Vector2> Position = new InputSlot<Vector2>();

    [Input(Guid = "b15018a7-6c61-4cee-88fa-a43684ef5cc4")]
    public readonly InputSlot<float> OutsideLine = new InputSlot<float>();

    [Input(Guid = "8efc35ee-875b-4181-aa1d-bd705f82bea9")]
    public readonly InputSlot<float> InsideLine = new InputSlot<float>();

    [Input(Guid = "21a2292d-1969-41ee-a71f-00a1621f9539")]
    public readonly InputSlot<string> FontPath = new InputSlot<string>();

    [Input(Guid = "b8035f87-f293-4da0-8bdc-ec75cc6252e7")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "e6164880-eae5-468e-8dd8-084d20918bc9")]
    public readonly InputSlot<float> Spacing = new InputSlot<float>();

    [Input(Guid = "0abd9f81-bc91-4775-bf0e-472399b9778e")]
    public readonly InputSlot<float> LineHeight = new InputSlot<float>();

    [Input(Guid = "e148fac5-d905-4e74-a958-5ed45b152ae1", MappedType =  typeof(VerticalAligns))]
    public readonly InputSlot<int> VerticalAlign = new InputSlot<int>();

    [Input(Guid = "617108fa-e7a6-478d-852b-cd5baa7d0b1c", MappedType = typeof(HorizontalAligns))]
    public readonly InputSlot<int> HorizontalAlign = new InputSlot<int>();

    [Input(Guid = "a827c2c6-3286-4488-9a90-ed38c4184e6a")]
    public readonly InputSlot<CullMode> CullMode = new InputSlot<CullMode>();

    [Input(Guid = "2cb6bb45-a74a-4eba-a2f0-2895e8e38b07")]
    public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "1b9a9ef5-fe48-4793-b420-7718643035f0")]
        public readonly InputSlot<int> Sharpness = new InputSlot<int>();
}