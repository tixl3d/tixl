namespace Lib.point.modify;

[Guid("18251874-5d5a-4384-8dcd-fcf297e54886")]
internal sealed class FilterPoints : Instance<FilterPoints>
{

    [Output(Guid = "bb886ff1-31a9-47aa-a39a-fa60ebb6c2d6")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "3b193782-2a56-4031-a0c6-9ebb576e66a5")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();

    [Input(Guid = "519d77c0-5605-433a-b8ce-6d84f99edd7b")]
    public readonly InputSlot<int> Count = new InputSlot<int>();

    [Input(Guid = "f32458a5-f19f-487b-8ae2-a575de0b4ff2")]
    public readonly InputSlot<int> StartIndex = new InputSlot<int>();

    [Input(Guid = "af0758b6-3876-4c95-a80f-0233b96bc1a7")]
    public readonly InputSlot<float> Step = new InputSlot<float>();

    [Input(Guid = "60cea2c3-02ae-4132-ad11-3f16c2f71b6e")]
    public readonly InputSlot<float> ScatterSelect = new InputSlot<float>();

    [Input(Guid = "537e6055-9c33-4b14-aa17-b34fd9d6bb61")]
    public readonly InputSlot<int> Seed = new InputSlot<int>();
}