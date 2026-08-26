using T3.Core.Utils;

namespace Lib.image.fx.stylize;

[Guid("299e9912-2a6a-40ea-aa31-4c357bbec125")]
internal sealed class Dither : Instance<Dither>
{
    [Output(Guid = "dac50008-a681-4e9a-8a71-e5f4f49a8eb5")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "4167d4a6-7f8c-4bc9-9424-a54388d2560b")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "0a3e1e60-39c1-4cb7-a6c1-6442ef0fe9cd")]
    public readonly InputSlot<Vector4> ShadowColor = new InputSlot<Vector4>();

    [Input(Guid = "703cf883-ece6-42d6-9e8c-0248b58eca2d")]
    public readonly InputSlot<Vector4> HighlightColor = new InputSlot<Vector4>();

    [Input(Guid = "9364f41d-0700-4c43-ad16-569081f510cf", MappedType = typeof(Methods))]
    public readonly InputSlot<int> Method = new InputSlot<int>();

    [Input(Guid = "1c8ca868-0d54-4776-92bd-5c4183660216")]
    public readonly InputSlot<Vector4> GrayScaleWeights = new InputSlot<Vector4>();

    [Input(Guid = "3a6f87d6-913b-4200-807c-bb4da3f64fb7")]
    public readonly InputSlot<Vector2> GainAndBias = new InputSlot<Vector2>();

    [Input(Guid = "41d59091-a974-43ed-b45b-0849fb91f6d1")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "50335511-3b96-4c23-a604-3bc3dacae062")]
    public readonly InputSlot<Vector2> Offset = new InputSlot<Vector2>();

    [Input(Guid = "0fb3a599-897f-4d38-a284-50374118810f", MappedType = typeof(SharedEnums.RgbBlendModes))]
    public readonly InputSlot<int> BlendMethod = new InputSlot<int>();

    private enum Methods
    {
        FloydSteinberg,
        Diffusion,
    }
}