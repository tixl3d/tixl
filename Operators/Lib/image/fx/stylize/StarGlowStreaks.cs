using T3.Core.Utils;

namespace Lib.image.fx.stylize;

[Guid("ecbb40c4-aef4-49a8-ac89-e82c3a09862f")]
internal sealed class StarGlowStreaks : Instance<StarGlowStreaks>
{
    [Output(Guid = "a256b06b-1500-4189-9820-906addbf387e")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "afcba3d4-b7db-40bd-919a-d4f49b1bf7fc")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "87148268-6d28-4d71-a2b5-b1ec0fb66685")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();

    [Input(Guid = "c643292c-5612-408a-9fd6-ce31e4de3f56")]
    public readonly InputSlot<float> Range = new();

    [Input(Guid = "68e16fd7-fabf-446e-9553-a737422c026b")]
    public readonly InputSlot<float> Brightness = new();

    [Input(Guid = "8c1d41ff-02e8-481b-a21a-56d1c519d920")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "dd0e21b8-91a6-4853-9907-e0f675a05a5d", MappedType = typeof(SharedEnums.RgbBlendModes))]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "6bc1a296-1a17-44a2-ba41-2c51c192090c")]
    public readonly InputSlot<System.Numerics.Vector4> OriginalColor = new();

    [Input(Guid = "943f048f-4938-4cde-9ac3-c7de5242450e")]
    public readonly InputSlot<int> Quality = new InputSlot<int>();

    [Input(Guid = "069a3776-3084-4a13-aee3-dd9fe6c6c9e1", MappedType = typeof(Methods))]
    public readonly InputSlot<int> GlareModes = new InputSlot<int>();

    private enum Methods
    {
        Diagonals,
        Cross,
        Star,
        Horizontal,
        Vertical
    }
}