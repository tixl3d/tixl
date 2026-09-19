namespace Examples.user.still.worksforeverybody.elements;

[Guid("4aaa90f5-b9ea-4654-957d-dace5850c776")]
internal sealed class _DoubleArrowLinePoint : Instance<_DoubleArrowLinePoint>
{
    [Output(Guid = "ad163f27-d9c1-4326-94c8-2bc59e19b903")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "7acdfd60-0e3f-4866-ac37-bfd8efb34e5f")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "b28254bc-0fcc-4343-80c2-63fbf6f5cae8")]
    public readonly InputSlot<float> LineHeight = new();

    [Input(Guid = "abd85a2d-c730-4010-90b7-9cabc0aa064d")]
    public readonly InputSlot<float> EndOffset = new();


}