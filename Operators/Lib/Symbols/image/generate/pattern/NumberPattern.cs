namespace Lib.image.generate.pattern;

[Guid("1fec51bc-4de7-400a-8910-db39f4129579")]
internal sealed class NumberPattern : Instance<NumberPattern>
{
    [Output(Guid = "569ef449-6919-4e6f-880e-6f26c6fd2a5e")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "8a3681fa-4820-4855-9a12-c3740772f4d8")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "90620390-0fc4-4f2f-9c8f-04c6a75ee19e")]
    public readonly InputSlot<Vector4> TextColor = new();

    [Input(Guid = "79d612f0-0b6e-47a8-9e9b-ee2894c85cc8")]
    public readonly InputSlot<Vector4> LineColor = new();

    [Input(Guid = "dd187262-d769-4694-8ca4-a2c88a4667fd")]
    public readonly InputSlot<Vector4> Highlight = new();

    [Input(Guid = "3343385a-18eb-413e-a6b3-fe4d20a13260")]
    public readonly InputSlot<Vector4> OriginalImage = new();

    [Input(Guid = "2b0bf570-d839-4514-9e70-0956f4ffb8f7")]
    public readonly InputSlot<Vector2> CellSize = new();

    [Input(Guid = "72a70b15-0342-427a-8c2f-e02ef31b2677")]
    public readonly InputSlot<Vector2> CellRange = new();

    [Input(Guid = "fee40e14-e8b1-4bfa-abe1-9de7a6ed43f1")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "d4adfa55-97d8-4c42-a2fc-4c89f6c43c8f")]
    public readonly InputSlot<float> Offset = new();

    [Input(Guid = "8a123978-eca8-4059-8381-b0983ad74223")]
    public readonly InputSlot<float> ScrollOffset = new();

    [Input(Guid = "80ecefe9-adbf-4d12-a805-b3d41d667402")]
    public readonly InputSlot<float> ScrollSpeed = new();

    [Input(Guid = "34c56d90-6b8d-413e-9ced-68a8e7db3330")]
    public readonly InputSlot<float> HighlightThreshold = new();
}