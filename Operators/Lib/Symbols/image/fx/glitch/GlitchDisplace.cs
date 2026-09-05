namespace Lib.image.fx.glitch;

[Guid("43f15919-f6c3-4a10-9092-00973fc8e821")]
internal sealed class GlitchDisplace : Instance<GlitchDisplace>
{

    [Output(Guid = "4808ce68-4785-4d25-a2e2-68f6c89ae577")]
    public readonly Slot<Texture2D> Output2 = new();

    [Input(Guid = "7914bb8b-8444-4438-a156-b00d099ce659")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "de2930b4-bc0a-401f-a8b5-933d0d2297bc")]
    public readonly InputSlot<int> Rows = new InputSlot<int>();

    [Input(Guid = "8a966901-645f-4873-a4c5-8d53a75b3c60")]
    public readonly InputSlot<int> Columns = new InputSlot<int>();

    [Input(Guid = "502e7ba7-4824-4928-9e15-cbb060e73b05")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "20f149ee-123f-4347-ba8e-f403a3eae7d3")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

    [Input(Guid = "c0b5e7e3-278d-4abd-aa2d-964a47fb2fcf")]
    public readonly InputSlot<float> Threshold = new InputSlot<float>();

    [Input(Guid = "5e591643-7c92-4fbb-8e14-4954f9493236")]
    public readonly InputSlot<Vector2> Stretch = new InputSlot<Vector2>();

    [Input(Guid = "531556ad-5d4d-4110-b8ea-be1fd6d443fd")]
    public readonly InputSlot<Vector2> Offset = new InputSlot<Vector2>();

    [Input(Guid = "990ac61f-09b7-42ad-a2ed-4fa27b7e491b")]
    public readonly InputSlot<Vector2> Scatter = new InputSlot<Vector2>();

    [Input(Guid = "2e5c0cd5-8c34-49b1-b67e-1a6bc006b2b2")]
    public readonly InputSlot<Vector2> ScatterStretch = new InputSlot<Vector2>();

    [Input(Guid = "2866ef59-644d-4af5-bb95-0d028b01bb47")]
    public readonly InputSlot<Vector2> ScatterOffset = new InputSlot<Vector2>();

    [Input(Guid = "1bd2e0bd-6902-44e3-93ce-da352973ca8d")]
    public readonly InputSlot<Vector4> Colorize = new InputSlot<Vector4>();

    [Input(Guid = "6a1efc82-7ca4-4c79-a3f9-f16b568c3131")]
    public readonly InputSlot<float> ColorRatio = new InputSlot<float>();

    [Input(Guid = "58f7ea26-2091-4b8f-8458-1d7e4a5b7699")]
    public readonly InputSlot<int> Seed = new InputSlot<int>();

    [Input(Guid = "26c70c16-ba58-4dfe-93e6-e39bd6442485")]
    public readonly InputSlot<BufferWithViews> OverridePoints = new InputSlot<BufferWithViews>();

    [Input(Guid = "3fdfce77-8622-4fcf-a7cf-e4bfbabc280c", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new InputSlot<int>();

    private enum Modes
    {
        Static,
        Highlights,
        Shadows,
        EdgesLeft,
        EdgesRight,
    }
}