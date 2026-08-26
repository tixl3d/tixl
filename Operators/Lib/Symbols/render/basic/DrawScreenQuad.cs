using T3.Core.Utils;

namespace Lib.render.basic;

[Guid("5a2752e8-95ae-4d76-b903-1f52ef43bcdc")]
internal sealed class DrawScreenQuad : Instance<DrawScreenQuad>
{
    [Output(Guid = "3c8116a2-2686-41ba-8bfd-d1b3fb929b02")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "92c66734-dce9-402a-95f6-cde0e58bf32f")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "4e8fecd0-00ca-404e-a9d4-1cb0d3e044f1")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "a680706a-3e0f-4b93-9953-05df8d32109a")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "4ca612b7-a899-4567-98e4-8b6e96d5f251")]
    public readonly InputSlot<float> Height = new();

    [Input(Guid = "60cef5fb-b22a-4fa0-83bb-093d0f1d5b56", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "6a91ba46-257d-4c3c-94c5-72cbc06ee816")]
    public readonly InputSlot<bool> EnableDepthTest = new();

    [Input(Guid = "2680baf9-3dbd-4ade-b109-19b3b0f1d40f")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "6fd86a21-d06c-4edb-bbe2-39d7411e46d1")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "81f7acd7-6704-404b-b8be-9e77d2117fcc")]
    public readonly InputSlot<Filter> Filter = new();
}