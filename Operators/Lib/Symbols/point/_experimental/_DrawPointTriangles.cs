using T3.Core.Utils;

namespace Lib.point._experimental;

[Guid("b7db2c0f-8ea7-4c90-afe3-31ec7cdcc92f")]
internal sealed class _DrawPointTriangles : Instance<_DrawPointTriangles>
{
    [Output(Guid = "20f5b2c7-d621-4866-8728-ae4b6bcb149b")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "d0125887-e5f9-422f-883d-0c34d9b38a4b")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "228e18b6-cd51-40f4-ba44-c7996ebe184e")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "0c525f9c-fc55-4d6a-9b48-2942823cd306")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();

        [Input(Guid = "6096576a-4234-4037-b253-0cb544a2cc37", MappedType = typeof(WidthFXs))]
        public readonly InputSlot<int> WidthFactor = new InputSlot<int>();

        [Input(Guid = "4529319e-609d-449f-b320-f2c92c463bb5")]
        public readonly InputSlot<float> ShrinkWithDistance = new InputSlot<float>();

        [Input(Guid = "e1144012-ca01-4bfa-8581-7eb4e44c8638")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "4c92bcdd-3c11-4e14-ac8c-2f07474f4570")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "3fbe354e-7521-4442-bc7b-f36f1c613908", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMod = new InputSlot<int>();

        [Input(Guid = "d8efac0b-92aa-401f-afc5-fa641aadb656")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "426696f2-55b0-4ecc-9638-342338b38d6e")]
        public readonly InputSlot<float> TransitionProgress = new InputSlot<float>();

        [Input(Guid = "509929a0-745f-41cd-bc56-307ec9f3b606")]
        public readonly InputSlot<T3.Graphics.Compat.TextureAddressMode> WrapMode = new InputSlot<T3.Graphics.Compat.TextureAddressMode>();

        [Input(Guid = "ebcd896d-3292-450a-a020-8cf8456d83ce")]
        public readonly InputSlot<float> UvScale = new InputSlot<float>();

        [Input(Guid = "a7c18db7-3a61-420b-8627-7969693a74d7", MappedType = typeof(UvModes))]
        public readonly InputSlot<int> UvScaleFX = new InputSlot<int>();

        [Input(Guid = "14d08738-f71d-49c1-be46-0dab75245e9e")]
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