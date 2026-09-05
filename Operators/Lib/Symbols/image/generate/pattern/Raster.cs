namespace Lib.image.generate.pattern;

[Guid("4f89b41b-1643-442b-bec8-9f9ef2173baa")]
internal sealed class Raster : Instance<Raster>
{
    [Output(Guid = "44a192a6-ece7-4501-81d6-d0cc1d50bb93")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "cce27287-9774-436c-9e90-1becb42d8f95")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "b32e1383-07c4-4dc4-859d-e7fb19261bce")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "52d36eba-d789-41a0-bdf4-9ca6b3e32530")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "9cc926c7-ad28-4f49-a662-46584ae8ad9f")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "2221e831-12dd-4c9f-8533-48b9f96e60ab")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "a4350111-7ca8-4ce7-b494-3e82cc679999")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "0d0944a4-b5f6-4df5-8ef6-cbfe70fbae94")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "6b0bdd9a-248d-410f-9b0e-de20df937775")]
        public readonly InputSlot<float> MixOriginal = new InputSlot<float>();

        [Input(Guid = "fd22eaf6-ed3c-4e63-985c-3a3756ecaef2")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "27ce4875-b4da-4a44-a959-37c81c1600b8")]
        public readonly InputSlot<float> DotSize = new InputSlot<float>();

        [Input(Guid = "81b697d6-64b4-47ae-beee-84445b1e7240")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();

        [Input(Guid = "82e9ecfc-a000-4cdd-b856-0928228a3afa")]
        public readonly InputSlot<float> LineRatio = new InputSlot<float>();

        [Input(Guid = "54252e04-8f91-4ea5-91a6-f1f66f6da0a6")]
        public readonly InputSlot<float> Feather = new InputSlot<float>();

        [Input(Guid = "c1378bbd-70df-4b6e-876f-5f1f1c59a617")]
        public readonly InputSlot<float> RedToDotSize = new InputSlot<float>();

        [Input(Guid = "3329cd25-e7b9-47da-88e0-5dd7b6e324fa")]
        public readonly InputSlot<float> GreenToLineWidth = new InputSlot<float>();

        [Input(Guid = "92f52517-8ebc-4c89-af0a-b0bc5abc0439")]
        public readonly InputSlot<float> BlueToLineRatio = new InputSlot<float>();
}