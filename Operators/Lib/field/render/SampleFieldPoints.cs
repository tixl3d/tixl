namespace Lib.field.render;

[Guid("ef440c7d-9cbd-49b7-81b6-e1a42c6eda83")]
internal sealed class SampleFieldPoints : Instance<SampleFieldPoints>
{
    [Output(Guid = "0aa4a95e-3e3c-48be-8952-043e321b24d0")]
    public readonly Slot<BufferWithViews> Result2 = new();

        [Input(Guid = "a391513d-484a-471f-90bd-8a9af56c75d0")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "79d8da72-1558-44d2-9a91-a2a123dde2e3")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "309b0110-2724-4904-8545-8ab91b58212e", MappedType = typeof(FModes))]
        public readonly InputSlot<int> WriteTo = new InputSlot<int>();

    private enum Shapes
    {
        Sphere,
        Box,
        Plane,
        Zebra,
        Noise,
    }

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
}