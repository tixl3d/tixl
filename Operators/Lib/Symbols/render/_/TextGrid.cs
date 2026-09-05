namespace Lib.render.@_;

[Guid("ad28819d-be62-4ed7-932a-fc861562983d")]
internal sealed class TextGrid : Instance<TextGrid>
{
    [Output(Guid = "982bd425-e781-42ad-9c58-f026fb6f193c", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "7c3dbe80-da67-430b-9aaa-f2e2f34fed73")]
    public readonly InputSlot<string> Text = new();

    [Input(Guid = "1178c7c0-12ec-4284-9d28-c357f8e8a8ca")]
    public readonly InputSlot<Texture2D> DisplaceTexture = new();

    [Input(Guid = "a5b86357-0d11-4034-b19a-5c2d617bb261")]
    public readonly InputSlot<Int2> GridSize = new();

    [Input(Guid = "3ab49a26-3168-486a-be7e-35883659e0ef")]
    public readonly InputSlot<Vector2> CellSize = new();

    [Input(Guid = "d58039a7-12f2-46fa-b778-bfbb69027691")]
    public readonly InputSlot<Vector2> CellPadding = new();

    [Input(Guid = "0b5244ef-3588-48c4-9e26-195409fbaac8")]
    public readonly InputSlot<Vector2> TextOffset = new();

    [Input(Guid = "7f86c4ea-48d1-4d3a-9a5a-cfbca0da4daa")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "b49b85c9-9cc8-4118-a145-3d514108d3e2")]
    public readonly InputSlot<Vector3> DisplaceAmount = new();

    [Input(Guid = "94312d3c-ac74-4965-9874-ddd96d1437ff")]
    public readonly InputSlot<bool> WrapText = new();

    [Input(Guid = "7dc864f5-b6eb-4d99-815e-8920e86e36b9")]
    public readonly InputSlot<float> OverrideScale = new();

    [Input(Guid = "98b5699a-60fb-436e-93f1-d301a471dfe1")]
    public readonly InputSlot<Vector4> HighlightColor = new();

    [Input(Guid = "b1a5ff6b-dfa4-4fac-b676-e2ba38d6a9f8")]
    public readonly InputSlot<string> HighlightChars = new();
}