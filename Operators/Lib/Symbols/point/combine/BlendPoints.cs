namespace Lib.point.combine;

[Guid("2dc5c9d1-ea93-4597-a4d9-7b610aad603a")]
internal sealed class BlendPoints : Instance<BlendPoints>
{

    [Output(Guid = "660013c7-8f6b-458a-bb86-61e5a85692a4")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "97904d2e-ae67-4ab4-9201-7902a85d12f3")]
    public readonly InputSlot<BufferWithViews> PointsA_ = new InputSlot<BufferWithViews>();

    [Input(Guid = "91b903a2-5127-431b-ab66-d5a38ce1693c")]
    public readonly InputSlot<BufferWithViews> PointsB_ = new InputSlot<BufferWithViews>();

    [Input(Guid = "ba7ffda2-f9f6-440d-a174-7339844835fa")]
    public readonly InputSlot<float> BlendFactor = new InputSlot<float>();

    [Input(Guid = "EE8E9E15-CE18-4034-ABC6-DD56108C8A02")]
    public readonly InputSlot<float> Scatter = new InputSlot<float>();

    [Input(Guid = "c5480ce5-a8ba-4a26-8cee-c28e442020b7", MappedType = typeof(BlendModes))]
    public readonly InputSlot<int> BlendMode = new InputSlot<int>();

    [Input(Guid = "BDB712A8-3DBC-458A-887A-5ADD51813196")]
    public readonly InputSlot<float> RangeWidth = new InputSlot<float>();

    [Input(Guid = "ACEF877D-214D-4CA0-AC11-95FA59D1F6FC", MappedType = typeof(PairingModes))]
    public readonly InputSlot<int> Pairing = new InputSlot<int>();

        
    private enum BlendModes
    {
        Mix,
        UseA_F1,
        UseB_F1,
        MixWithSlidingRange,
        MixWithSlidingRangeSmooth,
    }
        
    private enum PairingModes
    {
        WrapAround,
        Adjust,
    }
        
}