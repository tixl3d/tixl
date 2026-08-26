namespace Lib.point.modify;

[Guid("e570b2e6-6e35-4a14-ade6-f377494fe96d")]
internal sealed class ClearSomePoints : Instance<ClearSomePoints>
{

    [Output(Guid = "769cc00b-f190-4c90-ace3-3ec10cb156dd")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "f1662ff8-2a3c-40c4-a313-4c8b831830d7")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();

    [Input(Guid = "168cb238-fdd9-4302-ad2d-bcfe0f200525")]
    public readonly InputSlot<float> Ratio = new InputSlot<float>();

    [Input(Guid = "ed9306bb-5ca8-4cfc-acb9-7333821f651f")]
    public readonly InputSlot<int> Seed = new InputSlot<int>();

    [Input(Guid = "68a2ea07-4ca9-4211-a8e5-e67943c7d3fa")]
    public readonly InputSlot<int> Repeat = new InputSlot<int>();

    [Input(Guid = "23302290-2789-49da-9b65-6d5b472c94e8")]
    public readonly InputSlot<int> Resolution = new InputSlot<int>();


    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
    }

    private enum OffsetModes
    {
        Add,
        Scatter,
    }
        
    private enum Interpolations
    {
        None,
        Linear,
        Smooth,
    }
}