namespace Lib.point.modify;

[Guid("3d958f08-9c0f-45eb-a252-de880b5834f3")]
internal sealed class CustomPointShader : Instance<CustomPointShader>,ITransformable
{
    public CustomPointShader()
    {
        Output.TransformableOp = this;
    }
        
    public IInputSlot TranslationInput => Offset;
    public IInputSlot RotationInput => null;
    public IInputSlot ScaleInput => null;
        
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }        
        
    [Output(Guid = "e0097148-4395-4441-83d2-c5cf5b76bb61")]
    public readonly TransformCallbackSlot<BufferWithViews> Output = new();

        [Input(Guid = "e77660e2-0fd0-45ea-8e0b-c607a757bb49")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "01898885-4140-4435-bb44-a7a6f6f32657")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a5c7863e-9c26-4109-9851-3244086b0ccc")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "e5a7649f-684e-4938-8ae3-7289f5b9ff45")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "b909844f-cff7-4907-9bc8-e9c2281582bf")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "20226539-a481-4df6-8dc7-cc65de915ea9")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "362e446c-b471-40fc-a21f-ddc25dafd01b")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "776b96c2-ec0d-4668-9112-39badbf5b619")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "377151fc-49a7-4407-bf85-74c4a7d5188d")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "e9712b03-e7aa-4fe5-b5cf-f2c5d0c0b0df")]
        public readonly InputSlot<string> ShaderCode = new InputSlot<string>();

        [Input(Guid = "864dfe5a-14f2-490f-be9c-ee13ad58605c")]
        public readonly InputSlot<string> AdditionalDefines = new InputSlot<string>();

        [Input(Guid = "fe120fea-dc5e-45ed-97b5-09d9d4e6269c")]
        public readonly InputSlot<string> TemplateFile = new InputSlot<string>();

        [Input(Guid = "2a0cf45c-bcf5-4802-b32b-84cef0f1c129")]
        public readonly InputSlot<int> Count = new InputSlot<int>();

        [Input(Guid = "d669b4e9-8a3e-493c-93d1-da0267b41b1b")]
        public readonly MultiInputSlot<SharpDX.Direct3D11.ShaderResourceView> ShaderResources = new MultiInputSlot<SharpDX.Direct3D11.ShaderResourceView>();

        [Input(Guid = "aa12212a-f1e4-4efb-b3b5-b6861f09f66b")]
        public readonly MultiInputSlot<SharpDX.Direct3D11.Buffer> ConstantBuffers = new MultiInputSlot<SharpDX.Direct3D11.Buffer>();

        [Input(Guid = "6e872357-4c12-4c4f-acce-dd7ef3297446")]
        public readonly InputSlot<float> TimeScale = new InputSlot<float>();

        [Input(Guid = "18d79780-050c-4914-b1e3-ac2a164e45ea")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Output(Guid = "3b249524-d36c-4502-8de5-d519799041b8")]
        public readonly Slot<string> ShaderCode_ = new Slot<string>();
}