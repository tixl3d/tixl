namespace Lib.image.fx.stylize;

[Guid("c68fbb84-2f56-4aed-97ab-3c2df0ec700b")]
internal sealed class MosiacTiling : Instance<MosiacTiling>
{
    [Output(Guid = "0ce69c7f-29c9-461b-b593-402c4f9131e8")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "5a5ebec0-88ac-4e8e-873b-88e5d68fb920")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "58ed6ff5-4a5e-452d-ab60-efdfbccc3413")]
    public readonly InputSlot<Texture2D> FxTextures = new InputSlot<Texture2D>();

    [Input(Guid = "732f95f6-c0f3-4cee-bd0c-c8144b5b4c63")]
    public readonly InputSlot<Vector2> Center = new InputSlot<Vector2>();

    [Input(Guid = "746e0a30-8367-476e-97c9-f767aa47f6ad")]
    public readonly InputSlot<Vector2> Stretch = new InputSlot<Vector2>();

    [Input(Guid = "1ca6763a-cc17-499d-bfe5-aeac815bd8bb")]
    public readonly InputSlot<float> Size = new InputSlot<float>();

    [Input(Guid = "fae262db-2b93-4f4d-bc94-cb76b67b70cc")]
    public readonly InputSlot<float> SubdivisionThreshold = new InputSlot<float>();

    [Input(Guid = "79932d92-ccc2-48ed-b412-37698749636b")]
    public readonly InputSlot<int> MaxSubdivisions = new InputSlot<int>();

    [Input(Guid = "30791a22-3a50-451f-a080-cdb2cb48d87d")]
    public readonly InputSlot<float> Randomize = new InputSlot<float>();

    [Input(Guid = "a99725c2-9a88-4d9f-8b38-cb3f392d0be9")]
    public readonly InputSlot<float> Padding = new InputSlot<float>();

    [Input(Guid = "587c4737-46ad-4b92-9d14-21c0fc0da637")]
    public readonly InputSlot<float> Feather = new InputSlot<float>();

    [Input(Guid = "2f80e505-4543-44e8-8799-fe0c13a91a51")]
    public readonly InputSlot<Vector4> GapColor = new InputSlot<Vector4>();

    [Input(Guid = "b243743d-ea21-427b-befb-7130d9c3157a")]
    public readonly InputSlot<float> MixOriginal = new InputSlot<float>();
}