using T3.Core.Utils;

namespace Lib.image.generate;

[Guid("c7b8359e-6b36-4ae0-a6b4-4ac0d0306966")]
internal sealed class MunchingSquares2 : Instance<MunchingSquares2>
{
    [Output(Guid = "23e99d6d-b505-4517-8563-dc2846294210")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "c15eb60d-ec93-4d87-b349-4d67fe00f050")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "8a5d12fd-5f64-4df2-bcf1-ed4968e6420c")]
        public readonly InputSlot<System.Numerics.Vector4> ShadowColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "4b175d22-d1a4-4458-96d9-2a72ecd77ae0")]
        public readonly InputSlot<System.Numerics.Vector4> HighlightColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "11be1492-a848-4172-83dd-d19eea7e7c88", MappedType = typeof(Methods))]
        public readonly InputSlot<int> Method = new InputSlot<int>();

        [Input(Guid = "5cc518ea-736e-45ec-a27b-7e976262194e")]
        public readonly InputSlot<System.Numerics.Vector4> GrayScaleWeights = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "9750e62b-e650-4eac-9ff6-ec7c2640d6d5")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "c269b97f-3126-420b-87f5-a28ff01b3808")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "24dd4192-a1e8-4327-ab8e-88c1a34ef9b1")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "a9f57fc2-3613-40b8-8647-bd6b5812219f")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "fb02efe3-cca5-4664-b8ce-33ace59c51d6", MappedType = typeof(SharedEnums.RgbBlendModes))]
        public readonly InputSlot<int> BlendMethod = new InputSlot<int>();

        [Input(Guid = "3e373713-90bd-4d54-b4c8-6f8401efcd07")]
        public readonly InputSlot<int> Iterations = new InputSlot<int>();

        [Input(Guid = "d97df58f-09ff-4f96-b9ac-5844a454e5c9")]
        public readonly InputSlot<float> IterationFx = new InputSlot<float>();

    private enum Methods
    {
        Classic,
        Patterns,
        Or,
        Multiply,
        Chaos,
    }
}