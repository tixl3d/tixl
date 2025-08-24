namespace Lib.point.generate;

[Guid("945014cf-ba0b-40b3-85f9-f7deed70fa8d")]
internal sealed class PointTrail : Instance<PointTrail>
{

    [Output(Guid = "2a23b42c-ec03-401a-842a-6bdc0c633b7e")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

        [Input(Guid = "f22a4834-6333-4ed5-b07d-237692c61dc6")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "9a3998f9-f68a-4f8f-84dc-643e89f8c4f2")]
        public readonly InputSlot<int> TrailLength = new InputSlot<int>();

        [Input(Guid = "3b8c19dc-3d6a-4968-8192-8c95ce2f4e6d", MappedType = typeof(WriteToModes))]
        public readonly InputSlot<int> WriteTrailOrderTo = new InputSlot<int>();

        [Input(Guid = "274f1a1f-4dfa-4426-b53e-77c0c96cf7d8")]
        public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();

        [Input(Guid = "98366176-fdf3-42e1-afbc-a87fc0f9d82d")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "aaad0484-2d4f-4936-880d-67cd3aaab1b6")]
        public readonly InputSlot<bool> WriteLineSeparators = new InputSlot<bool>();

    private enum WriteToModes
    {
        None,
        Fx1,
        Fx2,
        Scale,
    }
}