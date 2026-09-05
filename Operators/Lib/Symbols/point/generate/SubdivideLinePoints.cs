namespace Lib.point.generate;

[Guid("c7f100bf-05a0-44af-9cf4-c5a1b5937e33")]
internal sealed class SubdivideLinePoints : Instance<SubdivideLinePoints>
{

    [Output(Guid = "ec73358e-9ac4-421c-b6c5-0c30b8101bb9")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "07a2be27-ff93-4cea-8fbe-1ce72ab8a1e1")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "835cf0fb-9958-4c60-9cf6-afc2f846b68a")]
    public readonly InputSlot<int> Count = new();

        [Input(Guid = "4c717acb-05d1-4fcb-84b5-a8fd8ad03cfc")]
        public readonly InputSlot<bool> ClosedShape = new InputSlot<bool>();


    private enum SampleModes
    {
        StartEnd,
        StartLength,
    }
}