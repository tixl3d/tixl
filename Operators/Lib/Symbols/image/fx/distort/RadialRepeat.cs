namespace Lib.image.fx.distort;

[Guid("d07a12c2-2c1a-4c88-95a2-53db21e66bac")]
internal sealed class RadialRepeat :Instance<RadialRepeat>{
    [Output(Guid = "38b28631-28b6-4687-9a3f-ba3e87120476")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "42451d9d-d1b1-49e2-813c-de01876ab8c5")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "f3050fbe-b1f5-467d-bed3-f19b3b5570ca")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "e9cb8bdb-ba6f-454a-bac7-c2eeb7a89d0d")]
        public readonly InputSlot<float> Slices = new InputSlot<float>();

        [Input(Guid = "7cf442ea-5382-42d0-a111-5293f7f2fc57")]
        public readonly InputSlot<float> Zoom = new InputSlot<float>();

        [Input(Guid = "30ec3cb0-9977-4b03-8be1-10cbeb31bbe0")]
        public readonly InputSlot<float> RotateImage = new InputSlot<float>();

        [Input(Guid = "787b9749-ebc5-4dcf-ac4e-c2226049e0ea")]
        public readonly InputSlot<float> RotateSlice = new InputSlot<float>();

        [Input(Guid = "f7067892-e0b4-45a9-969e-94c180d1ad91")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();
}