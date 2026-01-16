using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("6e89c814-cc67-40c2-933c-d03ff1b30ef7")]
internal sealed class DrawLinesAlt : Instance<DrawLinesAlt>
{
    [Output(Guid = "3b11b346-638d-47b5-8b2c-79e5cdaf7e34")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "d0a13a9f-3f20-4202-b599-7c0b7f03ff9c")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "9e56ca0c-f52f-4f4b-9293-78c8a1585e17")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "e2572f14-0eff-4a9e-b0d3-82ce20fcd95b")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();

        [Input(Guid = "92f4d16f-3100-4c8c-a7dc-37a9f2c548f8")]
        public readonly InputSlot<float> LineOffset = new InputSlot<float>();

        [Input(Guid = "aded76ab-47b3-4421-8939-114ede44805f", MappedType = typeof(WidthFXs))]
        public readonly InputSlot<int> WidthFactor = new InputSlot<int>();

        [Input(Guid = "7c62d13d-3df2-46bb-a725-29f52c609d99")]
        public readonly InputSlot<int> PointsPerShape = new InputSlot<int>();

        [Input(Guid = "ff59f227-283b-4469-904c-f3a5d565fdbb")]
        public readonly InputSlot<float> ShrinkWithDistance = new InputSlot<float>();

        [Input(Guid = "0bcf77ac-d6b2-443d-883e-2c05491145cf")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "3ad26a8c-18ee-4072-ae22-117ec7aa3b41")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "8c074334-d16f-409e-84c3-01b725c25526", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMod = new InputSlot<int>();

        [Input(Guid = "fb71c6b5-6882-40d4-bccd-86f13e0f6486")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "e5d4c25c-a1b0-46f3-b354-64de2331bd1c")]
        public readonly InputSlot<float> TransitionProgress = new InputSlot<float>();

        [Input(Guid = "584039ef-c876-432a-800c-b1366c45242b")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> WrapMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "73a384b1-f1f8-441f-a0a7-d40e4044e3d5")]
        public readonly InputSlot<float> UvScale = new InputSlot<float>();

        [Input(Guid = "41ec8023-d9f7-46d8-8705-d8043cce33ce", MappedType = typeof(UvModes))]
        public readonly InputSlot<int> UvScaleFX = new InputSlot<int>();

        [Input(Guid = "deb76a6f-484b-4732-b3ed-6e3be9d9a27a")]
        public readonly InputSlot<float> FadeOutTooLong = new InputSlot<float>();
        
        private enum WidthFXs
        {
            None,
            F1,
            F2,
        }

        private enum UvModes
        {
            Stretch,
            UFromFX1,
            UFromFX2,
        }
        
}