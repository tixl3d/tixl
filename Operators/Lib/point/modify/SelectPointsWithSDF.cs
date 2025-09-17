namespace Lib.point.modify;

[Guid("695d20dc-d1fe-4648-80fb-e1159b8aead4")]
internal sealed class SelectPointsWithSDF : Instance<SelectPointsWithSDF>
{
    [Output(Guid = "7a77a26e-88b5-43ed-84b1-b35682ceda7d")]
    public readonly Slot<BufferWithViews> Result2 = new();

        [Input(Guid = "af41409a-81ce-43c0-8cf7-1787e3b47b70")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "3da5b21f-f31b-4f11-9f1c-0deee0efac40")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "f38834a9-1bfe-4edc-9b70-2d0c57967556")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "74bc9dbf-015e-4182-95ef-cfca4be09f32", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "b0b20881-ccb2-46e6-b936-db892ac04202", MappedType = typeof(FModes))]
        public readonly InputSlot<int> WriteTo = new InputSlot<int>();

        [Input(Guid = "07177f13-ef76-4e81-91b5-49cf8eb73cbd", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "e3fe99fb-f1c3-4e73-ba7a-d260afcb3a22", MappedType = typeof(MappingModes))]
        public readonly InputSlot<int> Mapping = new InputSlot<int>();

        [Input(Guid = "1f929520-deb1-454e-a999-12dcdef29081")]
        public readonly InputSlot<float> Range = new InputSlot<float>();

        [Input(Guid = "b7bd5d38-03ed-46da-ae4f-bfea990104ae")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();

        [Input(Guid = "c9fbe013-7050-4714-a43d-68960d6afbea")]
        public readonly InputSlot<System.Numerics.Vector2> BiasAndGain = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "61acc5cc-f084-4c88-8d54-207645dc841f")]
        public readonly InputSlot<float> Scatter = new InputSlot<float>();

        [Input(Guid = "03558959-4190-4b5b-a544-b17d56fb77da")]
        public readonly InputSlot<bool> ClampNegative = new InputSlot<bool>();

        [Input(Guid = "bc002ec2-cd53-4cf3-800c-868f2cacbb0c")]
        public readonly InputSlot<bool> DiscardNonSelected = new InputSlot<bool>();



    private enum Modes
    {
        Override,
        Add,
        Sub,
        Multiply,
        Invert,
    }

    private enum FModes
    {
        None,
        F1,
        F2,
    }
    
    private enum MappingModes
    {
        Centered,
        FromStart,
        PingPong,
        Repeat,
    }
}