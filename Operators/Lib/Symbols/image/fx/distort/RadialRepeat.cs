namespace Lib.image.fx.distort;

[Guid("6dc6bfa3-ba30-4987-96c3-78b0da3ada62")]
internal sealed class RadialRepeat :Instance<RadialRepeat>{
    [Output(Guid = "2a036c9c-9d82-4582-af70-40cd3eb89fbe")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "43b610e7-7623-471b-b105-8008cba34a17")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "4cd2aef5-52dc-4025-bd16-ab5987caeaac")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "29014e97-bd03-4371-8ce9-334a6f474ab4")]
        public readonly InputSlot<float> Slices = new InputSlot<float>();

        [Input(Guid = "a0a55a6d-5b1f-4a38-94cf-478413eef3ed")]
        public readonly InputSlot<float> RotateSlice = new InputSlot<float>();

        [Input(Guid = "2e1a24f6-63b4-42ac-8a33-cb6e79ff2a7e")]
        public readonly InputSlot<float> Zoom = new InputSlot<float>();

        [Input(Guid = "e3bf3deb-8230-486e-9f19-30cb3753be5a")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "0d05623b-d71a-47fa-ad7c-7be339cc52bf")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();
}