namespace Lib.render.shading;

[Guid("e85d98cf-9240-4f5d-8df6-35425d325778")]
internal sealed class TextureToCubeMap : Instance<TextureToCubeMap>
{

    [Output(Guid = "a3c61268-e57c-4ab0-939c-6fc4da0fc574")]
    public readonly Slot<Texture2D> OutputTexture = new();

        [Input(Guid = "d5aa1045-5471-42c3-bfc2-c5fa9663817f")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "8c57c309-c033-4371-9647-dea3529e5655")]
        public readonly InputSlot<float> Orientation = new InputSlot<float>();

        [Input(Guid = "8eeb0224-ade3-4808-a60b-9c490e42229a", MappedType = typeof(Resolutions))]
        public readonly InputSlot<int> Resolution = new InputSlot<int>();


    private enum Resolutions
    {
        _4 = 4,
        _16 = 16,
        _32 = 32,
        _64 = 64,
        _128 = 128,
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }
}