namespace Lib.image.fx.distort;

[Guid("ca3f3c1b-6f22-4bf3-b06b-d2b0d85a8881")]
internal sealed class BubbleZoom : Instance<BubbleZoom>
{
    [Output(Guid = "f49bcb2b-106d-45f5-8161-8d4975b30f8c")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "019b7a1a-0f86-48e2-b5e9-9a04480a3b07")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "277c5e50-7d0f-416f-ba14-69ba407802dc")]
        public readonly InputSlot<System.Numerics.Vector2> Center = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "5bb5934d-28a8-4bd3-a146-f0ecb804170a")]
        public readonly InputSlot<float> Magnify = new InputSlot<float>();

        [Input(Guid = "1e93d2b1-d21c-44bd-a96c-9844b9a89c8f")]
        public readonly InputSlot<float> Feather = new InputSlot<float>();

        [Input(Guid = "0ce5b753-bcc0-4102-b011-64e603a50567")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> FeatherGradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "064b98dc-60f8-4019-a40c-21cb3bed9f4a")]
        public readonly InputSlot<float> Radius = new InputSlot<float>();

        [Input(Guid = "244d64c7-5f45-44a1-9644-b0a8b9e04261")]
        public readonly InputSlot<float> FlipEffect = new InputSlot<float>();

        [Input(Guid = "06f08ca0-c193-4ed0-9ca4-43d24b2a13ef")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "412055dc-88f7-4def-ac8e-76f7f424f905")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        

        [Input(Guid = "e5a5d0cf-447b-471f-871a-f3237a760df2")]
        public readonly InputSlot<float> Bias = new InputSlot<float>();
}