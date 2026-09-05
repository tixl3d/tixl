using T3.Core.Utils;

namespace Lib.render.sprite;

[Guid("122cbf32-b3e5-4db7-b18d-f2af5b10419c")]
internal sealed class DrawPointSpritesShaded : Instance<DrawPointSpritesShaded>
{
    [Output(Guid = "0ac5d7d5-e127-4464-9910-82deb4781c91")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "a305b4c3-fc34-4ccc-bff6-ab0eab58d768")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "a49cf849-802f-47fe-9e6c-7f53611d7a41")]
    public readonly InputSlot<BufferWithViews> Sprites = new();

    [Input(Guid = "1e2dbb8c-c164-49b3-b96a-b80655d5dcce")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "62dd6e4f-5cf5-4bd9-9683-8b9ed5d423f6")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "3602d9c6-6477-4a29-af63-2fb12c7efbb6")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "08fb9c79-2672-4ece-89bc-6f05e07592d7")]
    public readonly InputSlot<float> AlphaCutOff = new();

    [Input(Guid = "e7cd2998-cd5a-494e-8669-b68f59fba257")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "5e39b0d4-a268-4022-bfc1-1fdc9a98b48c", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMod = new();

    [Input(Guid = "9324404a-a4e3-46cf-b79a-722c6ab46fff")]
    public readonly InputSlot<CullMode> Culling = new();
        
    private enum TextureModes
    {
        RelativeStartEnd,
        StartRepeat,
        Tile,
        UseW,
    }
}