namespace Lib.image.generate.noise;

[Guid("5cbb8f47-5093-495d-bfb6-fbc5b00b7036")]
internal sealed class TileableNoise : Instance<TileableNoise>
{
    [Output(Guid = "a49707f3-5574-4db9-ba60-864e044a3ff2")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "7806875d-bfc9-47ce-9b14-163d0d4c7d59")]
        public readonly InputSlot<System.Numerics.Vector4> ColorA = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "43a48eb1-6cc4-44fa-8717-6d3687bb6efd")]
        public readonly InputSlot<System.Numerics.Vector4> ColorB = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "80acacd1-9b12-4564-b6b4-06ab4c8ba1f6")]
        public readonly InputSlot<int> Detail = new InputSlot<int>();

        [Input(Guid = "c1a6aad2-75a7-4c15-9bea-a8f8f5c69d73")]
        public readonly InputSlot<int> Octaves = new InputSlot<int>();

        [Input(Guid = "1d6d9bba-dddb-4130-bb92-a55d92c43b48")]
        public readonly InputSlot<float> Gain = new InputSlot<float>();

        [Input(Guid = "f1ed5a42-7366-4b6f-b286-f7195c6ae213")]
        public readonly InputSlot<float> Lacunarity = new InputSlot<float>();

        [Input(Guid = "91186518-2b7e-4def-92bc-ff56519c4883")]
        public readonly InputSlot<float> RandomPhase = new InputSlot<float>();

        [Input(Guid = "211aa522-d028-4873-8571-47021776543a")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "46741a9b-c45c-4f82-8a4f-551d826ac0ae")]
        public readonly InputSlot<float> Contrast = new InputSlot<float>();

        [Input(Guid = "c926feba-e2f9-4189-9cbc-b0f52d6a484f")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "1e245b3d-2aba-4f12-a235-3a5b5b7f0812")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "6c463d39-13a2-41c0-b015-cdfb1971f3ca")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "1f30596d-467d-4b28-a1d8-40ce61a14741")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        [Input(Guid = "048d3e2b-7d9b-4250-a69a-12f9ee22f6ca")]
        public readonly InputSlot<SharpDX.DXGI.Format> OutputFormat = new InputSlot<SharpDX.DXGI.Format>();


    private enum Methods
    {
        Legacy,
        OpenSimplex2S,
        OpenSimplex2S_NormalMap,
    }
}