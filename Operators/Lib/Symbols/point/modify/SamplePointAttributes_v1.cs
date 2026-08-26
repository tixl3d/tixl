namespace Lib.point.modify;

[Guid("b3de7a93-e921-4e43-8a56-6c84b2d18b74")]
internal sealed class SamplePointAttributes_v1 : Instance<SamplePointAttributes_v1>
,ITransformable
{
    [Output(Guid = "bdc65f4c-6eac-44bf-af39-6655605b8fae")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public SamplePointAttributes_v1()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "d42b8adc-f1d6-4f32-94a3-24802630d763")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "2b5ee35d-bb00-4dd1-abdc-c4584c7ce7c5", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Brightness = new InputSlot<int>();

        [Input(Guid = "ce9c0841-a1f3-4f5d-9cd8-636390112ece")]
        public readonly InputSlot<float> BrightnessFactor = new InputSlot<float>();

        [Input(Guid = "62e68350-739a-4220-bb84-70b33ea1baf0")]
        public readonly InputSlot<float> BrightnessOffset = new InputSlot<float>();

        [Input(Guid = "f5f424a1-42a4-4429-8d99-47a7c6176400", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Red = new InputSlot<int>();

        [Input(Guid = "1531e949-fe17-4718-9ac6-4dc3884c23fc")]
        public readonly InputSlot<float> RedFactor = new InputSlot<float>();

        [Input(Guid = "022f5d7d-b9fc-4c4f-8b7e-84aaedbfdd29")]
        public readonly InputSlot<float> RedOffset = new InputSlot<float>();

        [Input(Guid = "d87c9071-2636-4136-b488-410591be47c6", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Green = new InputSlot<int>();

        [Input(Guid = "6b05ba32-f445-4403-aaba-160c7876b03b")]
        public readonly InputSlot<float> GreenFactor = new InputSlot<float>();

        [Input(Guid = "f6b64f46-ce50-43cb-bc83-e1a3822067db")]
        public readonly InputSlot<float> GreenOffset = new InputSlot<float>();

        [Input(Guid = "c631114f-9ebc-4ea6-b6e5-4c999144e36c", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Blue = new InputSlot<int>();

        [Input(Guid = "d4a0385e-1dfb-4af0-bf52-5fab61f713bb")]
        public readonly InputSlot<float> BlueFactor = new InputSlot<float>();

        [Input(Guid = "b9cff4dd-52cd-4a36-ab17-b04794402d94")]
        public readonly InputSlot<float> BlueOffset = new InputSlot<float>();

        [Input(Guid = "b9203690-4052-4e06-8071-9e969a896d7e", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Alpha = new InputSlot<int>();

        [Input(Guid = "550b51f1-6f56-4fb8-88df-825a77938361")]
        public readonly InputSlot<float> AlphaFactor = new InputSlot<float>();

        [Input(Guid = "8432ce55-81d5-4791-b2a7-655c977985b1")]
        public readonly InputSlot<float> AlphaOffset = new InputSlot<float>();

        [Input(Guid = "d1f3b362-7ed4-4833-99e9-0fdc46ca2319")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "a82bc040-a398-41ed-93e1-74309d44a663")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "7e86bf8f-1d9d-4212-bf9f-987a03b55565")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "3afdb344-0a09-4907-89f1-447c991273da")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "9c53bca4-57fc-495f-ba07-02278c023680")]
        public readonly InputSlot<System.Numerics.Vector3> TextureRotate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "d5c1a82b-5633-446e-9836-a735a55c7a91")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> TextureMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "27fad3f7-a795-4d14-aa69-7f5e14159421")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

        [Input(Guid = "fcc59369-bb28-41e5-a7cf-452f0a844e77", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "5225DB75-5F9D-49F9-BCEB-0CBC8A56A3F4", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> RotationSpace = new InputSlot<int>();

        [Input(Guid = "D22095C2-15B5-4708-93B2-D6AE2DCD0DCA", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> TranslationSpace = new InputSlot<int>();


    private enum Attributes
    {
        NotUsed = 0,
        For_X = 1,
        For_Y = 2,
        For_Z = 3,
        For_W = 4,
        Rotate_X = 5,
        Rotate_Y = 6,
        Rotate_Z = 7,
        Stretch_X = 8,
        Stretch_Y = 9,
        Stretch_Z = 10,
    }

    private enum Modes
    {
        Add,
        Multiply,
    }
        
    private enum Spaces
    {
        Object,
        Point,
    }
}