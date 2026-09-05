namespace Lib.point.modify;

[Guid("09b2aeea-79fa-4209-860e-85161060b6c8")]
internal sealed class TransformWithImage : Instance<TransformWithImage>
,ITransformable
{
    [Output(Guid = "2fb36c90-2506-40bd-bbe9-34b3635c6411")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public TransformWithImage()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "1840ae3f-2dc4-4eb1-a3d0-de9c9d2e0afa")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "fccce788-f500-411d-94a4-2209c595bc2a")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "187f60f5-16e5-42c8-8359-8fabe18ec475", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "60335a52-8c05-4119-b80e-7a7f0f57de5c")]
        public readonly InputSlot<System.Numerics.Vector3> Translate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a01fb917-5c3a-4278-91aa-ff853183a6d5")]
        public readonly InputSlot<System.Numerics.Vector3> Scale = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "41d5b860-f420-41e2-b5c2-d8e168a8312f")]
        public readonly InputSlot<float> ScaleUniform = new InputSlot<float>();

        [Input(Guid = "027ef9a9-73ef-4d23-861b-660060541826")]
        public readonly InputSlot<System.Numerics.Vector3> Rotate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "9288acba-4ba5-4d5a-bfed-55c2e7a9b8d5")]
        public readonly InputSlot<float> ScaleFx1 = new InputSlot<float>();

        [Input(Guid = "6bc136e6-a452-4110-beab-7188e33b1bf8")]
        public readonly InputSlot<float> ScaleFx2 = new InputSlot<float>();

        [Input(Guid = "6fe19bb3-dd2d-4fcc-9ea6-18dbedfc7ede")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "f7c9329e-5931-4133-ae4c-9993e6148173")]
        public readonly InputSlot<int> Channel = new InputSlot<int>();

        [Input(Guid = "36e3ddee-c4ef-4b9f-a8a7-d543cc141e4b")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "b95b888a-4057-4011-981f-465945d11650")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "7cdab638-5110-4662-baee-b4ada8701242")]
        public readonly InputSlot<float> ImageScale = new InputSlot<float>();

        [Input(Guid = "55e9c165-08a4-4bfe-80a8-8dd19bf6cdf4")]
        public readonly InputSlot<System.Numerics.Vector3> TextureRotate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "1a3cde2d-2d12-475f-ab6b-ea44494c5aa0")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> TextureMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "531a30f9-ddf0-4a99-8cba-a9dc66626730")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

        [Input(Guid = "06e6e8d7-31d3-4607-8abe-a5170a05e1a9", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> TranslationSpace = new InputSlot<int>();

        [Input(Guid = "099019c1-f27c-44a4-9e9c-c22b8313310c")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "61f306a3-8799-452c-b9cf-4c71efdb633c")]
        public readonly InputSlot<float> Scatter = new InputSlot<float>();

        [Input(Guid = "386fa9e3-4dae-4c7c-9112-94f352415ddc")]
        public readonly InputSlot<float> StrengthOffset = new InputSlot<float>();

    
    private enum Modes
    {
        Add,
        Multiply,
    }
        
    private enum Spaces
    {
        Point,
        Object,
    }
    
    private enum FModes
    {
        None,
        F1,
        F2,
    }
}