namespace Lib.image.fx.distort;

[Guid("f13b6fbd-bd73-4aab-90fc-98d62372baa8")]
public sealed class FieldToImage : Instance<FieldToImage>
{
    [Output(Guid = "f9810ebe-8576-44cc-bb2f-b77d41905826")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "b42ee7e1-fa65-4231-917e-1a0824dc4a23")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "e2e65236-62c0-49b3-ad51-992e3953554f")]
        public readonly InputSlot<System.Numerics.Vector2> Center = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "2e07eea5-a7c1-4385-bfcb-da9e6c713e61")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "a253e4d8-ce08-44c0-8942-9e49788d4b52")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "c090441b-7ee6-426b-a37b-3a36555b9141", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "78e35af9-8c8a-4705-9740-4de28a6b5c82")]
        public readonly InputSlot<System.Numerics.Vector2> Range = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "ad76a32a-e3f2-4897-a7e6-2bb7479aa216")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "fd1a7b38-469c-47f0-9bcb-c6df99f49fd4")]
        public readonly InputSlot<bool> PingPong = new InputSlot<bool>();

        [Input(Guid = "f8bf7475-e55d-4c23-ac0e-cc3a053a4a2c")]
        public readonly InputSlot<bool> Repeat = new InputSlot<bool>();

        [Input(Guid = "d99d1e27-caba-4a9a-b689-8c965b9af89e")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Grandient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "9fab37c9-620a-4c53-8b67-b4d3937e3ba4")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "498f5af8-458b-45f7-8e70-e957f2bb994e")]
        public readonly InputSlot<SharpDX.DXGI.Format> OutputFormat = new InputSlot<SharpDX.DXGI.Format>();

        [Input(Guid = "5573ae7b-b699-42c5-84ac-2485040e9a55")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        private enum Modes
        {
            MapDistanceToColor,
            UseColor,
        }
}