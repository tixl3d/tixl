using T3.Core.DataTypes.ShaderGraph;

namespace Lib.point.modify;

[Guid("2fdb72b0-ac0b-4269-8822-e7d38497835a")]
internal sealed class PointColorWithField : Instance<PointColorWithField>
{
    [Output(Guid = "e9df0581-62c5-4d5d-9466-e88871d59dea")]
    public readonly Slot<BufferWithViews> Result2 = new();

        [Input(Guid = "39b6eee6-c9e0-4b48-8e37-ed11f4fbf326")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "924b6cc7-606b-4ff2-bcb9-ef3dcc9fe4d5")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> SdfField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "fbb45b98-1168-45ad-a10a-e7f5db96472c")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "a2d27386-0d39-4b93-aa9a-799b02241aab", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();



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