namespace Lib.point.transform;

[Guid("7f6c64fe-ca2e-445e-a9b4-c70291ce354e")]
internal sealed class TransformPoints : Instance<TransformPoints>, ITransformable
{
    [Output(Guid = "ba17981e-ef9f-46f1-a653-6d50affa8838")]
    public readonly TransformCallbackSlot<BufferWithViews> Output = new();

    public TransformPoints()
    {
        Output.TransformableOp = this;
    }        
    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Stretch;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "565ff364-c3d9-4c60-a9a0-79fdd36d3477")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "1ab4671f-7977-4e7e-bb06-f828ae32e3af", MappedType = typeof(Spaces))]
        public readonly InputSlot<int> Space = new InputSlot<int>();

        [Input(Guid = "9e803bd1-c5a3-4f6f-926d-d19f32dcbae5")]
        public readonly InputSlot<System.Numerics.Vector3> Translation = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "454d0150-dac4-41b2-83f8-d1ecc3ef76d4")]
        public readonly InputSlot<System.Numerics.Vector3> Rotation = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "523b7acd-d8e7-4473-9ec7-15eec1d795df")]
        public readonly InputSlot<System.Numerics.Vector3> Stretch = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "a6e5770b-39dc-4d7b-b92e-53302dc89395")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "0192b746-ff90-4c26-a7d4-754b6ec8006b")]
        public readonly InputSlot<bool> UpdateRotation = new InputSlot<bool>();

        [Input(Guid = "319d71a9-b8dd-406f-a3a2-1c7508ba2ca7")]
        public readonly InputSlot<System.Numerics.Vector3> Shearing = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "0ef7556a-950f-406c-8e1d-511d17b4ea10")]
        public readonly InputSlot<System.Numerics.Vector3> Pivot = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "fb2cfc5e-51d6-4efa-b936-bdb73c33549f")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "a2b65311-d1fd-491e-8787-2f9216f3574a", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "4af2dbdd-1005-465e-a193-756ed2b29a33")]
        public readonly InputSlot<float> ScaleW = new InputSlot<float>();

        [Input(Guid = "af0cff8a-126e-47bd-bb60-9198567f85e0")]
        public readonly InputSlot<float> OffsetW = new InputSlot<float>();

        [Input(Guid = "56cd97c5-f4f1-4eb4-a53c-312373ee7706")]
        public readonly InputSlot<bool> WIsWeight = new InputSlot<bool>();
        
        
        
    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
    }
    
    private enum FModes
    {
        None,
        F1,
        F2,
    }
        
        
        
        
        
}