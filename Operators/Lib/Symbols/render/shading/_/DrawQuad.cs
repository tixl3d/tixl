using T3.Core.Utils;

namespace Lib.render.shading.@_;

[Guid("b89c5923-cc58-4d7a-8dce-eb1f8cdfc6e8")]
internal sealed class DrawQuad : Instance<DrawQuad>
{
    [Output(Guid = "1ff16183-13b9-4c8f-a82a-77e8be0c641b", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "307e2b98-a337-4636-969f-a19841b11511")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "018dab29-db3b-49ee-872b-9042c4c0bced")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "105d474f-a54e-4350-8de6-8bfd4dc0b0dd")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "a2d39e5b-38c7-4751-bc29-7389f9e9d8e5")]
    public readonly InputSlot<float> Height = new();

    [Input(Guid = "08a058b0-9889-49d3-87a0-a1a98278eb06", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "a4630612-743f-4396-8e2f-982052d508f4")]
    public readonly InputSlot<bool> EnableDepthTest = new();

    [Input(Guid = "efc093f5-6fa4-4042-9cc3-6fdc96355a72")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "9fbee6ee-5933-48f6-84f0-1da5e4b744b2")]
    public readonly InputSlot<Comparison> Comparison = new();
}