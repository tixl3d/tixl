namespace Lib.image.fx.stylize;

[Guid("42e6319e-669c-4524-8d0d-9416a86afdb3")]
internal sealed class AsciiRender : Instance<AsciiRender>
{
    [Output(Guid = "b0b6a771-e1a4-4681-a8be-8ed7ac1f66c4")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "b7d24c9b-ad9e-4ba5-82d9-15414868cdd9")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> ImageA = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "9e093ac2-0dc0-4791-bb27-36d1f6ea1c47")]
        public readonly InputSlot<System.Numerics.Vector4> Fill = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "66509dd9-372b-4351-ad18-d7713d75cc01")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "68801326-950b-4675-8450-56abf64e8518")]
        public readonly InputSlot<float> MixInColors = new InputSlot<float>();

        [Input(Guid = "4623488a-cef2-4aaa-bfea-54e39e0b5653")]
        public readonly InputSlot<float> ScaleFactor = new InputSlot<float>();

        [Input(Guid = "8c6c08b8-0aa2-492a-b6e7-f49be41b3e6b")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "c7502cc9-bfe4-40d1-9b85-c6f59a15e675")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> FontCharSize = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "a842bd72-abb2-4207-8397-7e727aaa6c63")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "011db3c0-4b2b-4040-934a-c52f671b40fd")]
        public readonly InputSlot<float> Randomize = new InputSlot<float>();

        [Input(Guid = "7f35bc61-a27f-4a36-9ecc-b5944791f6f0")]
        public readonly InputSlot<string> FontFilePath = new InputSlot<string>();

        [Input(Guid = "4a300626-b562-4a5e-bfc5-9ffcd99dc0d5")]
        public readonly InputSlot<string> FilterCharacters = new InputSlot<string>();

        [Input(Guid = "03d335bb-e4c6-4154-a53b-ab1af64e301f")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

        [Input(Guid = "86bb9127-10fb-47cb-9aee-45be01567810")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

}