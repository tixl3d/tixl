using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("6b2f5350-6bb8-4f2c-92af-6e5fc81b8a82")]
internal sealed class DrawLinesShaded : Instance<DrawLinesShaded>
{
    [Output(Guid = "516f4176-8031-45b5-b8e6-d668495dc655")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "ad1cb6a8-ff4e-400a-a91f-0664f476223e")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "05dbd5e7-7794-410a-8b01-717cb5b451fa")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "2c273dce-e2b9-4124-a587-3b5ab721462e")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();

        [Input(Guid = "5aab3b9a-42c3-4e56-91a3-1dbe8d2866a4", MappedType = typeof(WidthFXs))]
        public readonly InputSlot<int> WidthFactor = new InputSlot<int>();

        [Input(Guid = "9ffb7462-b0eb-4c31-8c95-75b681f4161b")]
        public readonly InputSlot<float> ShrinkWithDistance = new InputSlot<float>();

        [Input(Guid = "510680d2-4239-49f8-bfd5-53ed57780b38")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "72b59edd-d7df-45b5-ba47-fc7a1fc50ceb")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "881099c9-2d8b-4c72-8cee-5f717ea5cbf4", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMod = new InputSlot<int>();

        [Input(Guid = "941ecbd9-a85b-4d70-ad01-96a56f9daf9b")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "a2211be9-2945-4ed3-8de5-d6704bab36fe")]
        public readonly InputSlot<float> TransitionProgress = new InputSlot<float>();

        [Input(Guid = "fd2ffbc0-f250-4327-97c8-1b0c98837c58")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> WrapMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "c6c819e9-a876-48ae-8103-8e89f30ff22d")]
        public readonly InputSlot<float> UvScale = new InputSlot<float>();

        [Input(Guid = "0a7d68e7-e66a-466e-9ab2-bd0cd335b055", MappedType = typeof(UvModes))]
        public readonly InputSlot<int> UvScaleFX = new InputSlot<int>();

        [Input(Guid = "47bcd837-d4f4-40bf-b725-e10711dae0e8")]
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