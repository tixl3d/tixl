namespace Lib.image.generate.noise;

[Guid("dc34c54b-f856-4fd2-a182-68fd75189d7d")]
internal sealed class ShardNoise : Instance<ShardNoise>
{
    [Output(Guid = "7aa58fd2-2bf4-41a3-8eea-269a082c93a8")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "2c6a429d-5a38-4457-94d5-8994e7d1242d")]
        public readonly InputSlot<System.Numerics.Vector4> ColorA = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "5d02399d-dcfa-41c0-bc6b-59833389b580")]
        public readonly InputSlot<System.Numerics.Vector4> ColorB = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "bff37ed2-cc5a-47fa-9363-65bb1d5eb2fa")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "f5233be8-dfdf-463b-af49-029f3459bbeb")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "3949fce5-e285-4787-9db6-0ef2e533b15e")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "4109631f-850a-4d65-a3b9-47e168884c41")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "3abec2c0-a46b-439a-8b00-21e3e9a36933")]
        public readonly InputSlot<System.Numerics.Vector2> Direction = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "12d39fbe-cea2-44af-acf7-901df793e0c4")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "58ff3aee-7ced-4634-ac4a-871d67d96b57")]
        public readonly InputSlot<float> Sharpen = new InputSlot<float>();

        [Input(Guid = "0b76c7f3-32b0-4aa0-a17d-16cb8bde5f80")]
        public readonly InputSlot<float> Rate = new InputSlot<float>();

        [Input(Guid = "35852bd6-be0a-4dae-b078-b6db734ca772", MappedType = typeof(Methods))]
        public readonly InputSlot<int> Method = new InputSlot<int>();

        [Input(Guid = "d2e58300-b054-4df5-b3e8-5d7fb36d1ebc")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "1583d95b-da27-4d12-9937-8cd99a61bb18")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();


    private enum Methods
    {
        Cubism,
        Cubism_X_Octaves,
        Octaves,
    }
}