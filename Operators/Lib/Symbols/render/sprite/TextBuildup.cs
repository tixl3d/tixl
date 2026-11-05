namespace Lib.render.sprite;

[Guid("09adcf34-0afe-46a2-b55a-24275ae7c32c")]
internal sealed class TextBuildup : Instance<TextBuildup>
{
    [Output(Guid = "ec8d3b54-32ea-4180-8759-b355bc1ea463")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "144de0c1-93c2-4e1f-a6fa-4c632006af13")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "30d9bb95-1577-461b-9199-5ccf754d36cd")]
    public readonly InputSlot<BufferWithViews> Sprites = new();

    [Input(Guid = "8fd700f3-875a-4412-ad1d-2b8e867aa6f6", MappedType = typeof(Units))]
    public readonly InputSlot<int> Unit = new();

    [Input(Guid = "cd867336-0aca-47e1-a8fd-7559c143b8c2")]
    public readonly InputSlot<float> RangeEnd = new();

    [Input(Guid = "c3bf5524-8487-4603-bff9-6aed9246aa61")]
    public readonly InputSlot<float> RangeStart = new();

    [Input(Guid = "ff39bf17-e051-4bc7-92a6-bc9685b25cca")]
    public readonly InputSlot<float> Spread = new();

    [Input(Guid = "38b4009b-7f3f-4ec6-87ee-0928e2f01d0f")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "0fc0df7c-ff8c-413f-a218-93a1a1a8cc42")]
    public readonly InputSlot<float> Randomize = new();

    [Input(Guid = "e1a048e7-81e5-4ec6-a822-c6af9d8a0476")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "62aa1538-7fe3-45b7-9e7b-7fd4ade3de69")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "0fdd6dd6-2594-48f4-a96b-d5efb0d13618")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "36be48ba-ee05-4370-a8a8-58f18cc90a98")]
    public readonly InputSlot<Vector2> Scale = new();

    [Input(Guid = "be8618a5-388b-469f-831d-8b5dbf8a6863")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "98d520ff-5fe1-4787-89f6-a081eb742f7b")]
    public readonly InputSlot<Vector2> Pivot = new();

    [Input(Guid = "20d19099-d693-488f-bc7f-5a9c269c391e")]
    public readonly InputSlot<float> OutDirection = new();

    [Input(Guid = "8a3f6d2e-41c7-4b8e-9a52-6c1e0f7d9b34")]
    public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new();

    private enum Units
    {
        Characters,
        Groups,
    }
}