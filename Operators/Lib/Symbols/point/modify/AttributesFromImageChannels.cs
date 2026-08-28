namespace Lib.point.modify;

[Guid("1c54fdad-9eb4-49e8-8fde-1c78b0fa2b48")]
internal sealed class AttributesFromImageChannels : Instance<AttributesFromImageChannels>
,ITransformable
{
    [Output(Guid = "1fc67380-4e34-48c1-a1bf-e6f6d145aed0")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

    public AttributesFromImageChannels()
    {
        OutBuffer.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => TextureRotate;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "ad53eb20-1e8b-4c69-9ed2-b5e20da14a50")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "68087e68-504d-4b89-aa76-199473c893c2", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Brightness = new InputSlot<int>();

        [Input(Guid = "c91340d9-faf9-4c5a-a0c9-34a93c2e2d12")]
        public readonly InputSlot<float> BrightnessFactor = new InputSlot<float>();

        [Input(Guid = "7f1959f5-903b-4521-aea6-53decfcf28e5")]
        public readonly InputSlot<float> BrightnessOffset = new InputSlot<float>();

        [Input(Guid = "bb19d1e6-e9c0-451d-9f8a-9d2094d7af9d", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Red = new InputSlot<int>();

        [Input(Guid = "88978d11-4227-4795-861b-e0fc92fecb78")]
        public readonly InputSlot<float> RedFactor = new InputSlot<float>();

        [Input(Guid = "88b13f5a-24c7-484b-b6d7-6392bddaf0b8")]
        public readonly InputSlot<float> RedOffset = new InputSlot<float>();

        [Input(Guid = "faf18518-6ce5-4af2-906c-b31176c52b0d", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Green = new InputSlot<int>();

        [Input(Guid = "98415cd6-b1ea-47a7-ade7-4b37e9f301da")]
        public readonly InputSlot<float> GreenFactor = new InputSlot<float>();

        [Input(Guid = "b6b0362c-2894-475b-a668-9de951dcaf04")]
        public readonly InputSlot<float> GreenOffset = new InputSlot<float>();

        [Input(Guid = "69f76f56-9e6c-4fab-9f30-55eb5ddc2926", MappedType = typeof(Attributes))]
        public readonly InputSlot<int> Blue = new InputSlot<int>();

        [Input(Guid = "09e4c062-db18-4249-a34d-2e1a7d0ff7e5")]
        public readonly InputSlot<float> BlueFactor = new InputSlot<float>();

        [Input(Guid = "91739865-4711-40e6-8f4f-a94780be1a41")]
        public readonly InputSlot<float> BlueOffset = new InputSlot<float>();

        [Input(Guid = "9093530f-e5c6-48d3-ba2f-d528a5548bfa")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "b1013848-0dcd-4d74-9b49-d5ef4ef539d3")]
        public readonly InputSlot<System.Numerics.Vector3> Center = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "5154b839-f530-459d-950d-81c992a6381b")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "791a6c0e-190b-46d1-ad91-66d6c2f6748b")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "81f273e7-9704-4648-9b66-50c51bbe7c19")]
        public readonly InputSlot<System.Numerics.Vector3> TextureRotate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "82940438-3f90-44a1-b7bd-0b8332c2ace9")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> TextureMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "36a6f356-0e8c-4df1-9079-27ff496b4d75")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

        [Input(Guid = "e864651e-06df-427d-978e-5adaa5366d0c", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> RotationSpace = new InputSlot<int>();

        [Input(Guid = "f936817b-b430-4bd2-b373-c5242261ce9e", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> TranslationSpace = new InputSlot<int>();

        [Input(Guid = "6957dfd9-bbc3-49b9-bd22-a7d4540bbe50", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "44b76ec7-8f8c-4f49-a8ac-e945b5ccf884")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "e29b049f-431a-4590-81d5-f42ff9762acf", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "93f5697b-943f-482a-95b8-26db776bb7ce")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();


    private enum Attributes
    {
        NotUsed = 0,
        X = 1,
        Y = 2,
        Z = 3,
        F1 = 4,
        F2 = 5,
        Rotate_X = 6,
        Rotate_Y = 7,
        Rotate_Z = 8,
        Scale_Uniform= 9,
        Scale_X = 10,
        Scale_Y = 11,
        Scale_Z = 12,
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
    
    private enum FModes
    {
        None,
        F1,
        F2,
    }
}