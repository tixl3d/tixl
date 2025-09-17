using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("836f211f-b387-417c-8316-658e0dc6e117")]
internal sealed class DrawLines : Instance<DrawLines>
{
    [Output(Guid = "73ebf863-ba71-421c-bee7-312f13c5eff0")]
    public readonly Slot<Command> Output = new();

        [Input(Guid = "e15b6dc7-aaf9-4244-a4b8-4ac13ee7d23f")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "75419a73-8a3e-4538-9a1d-e3b0ce7f8561")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "df158fcf-3042-48cf-8383-7bf4c1bcb8a6")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();

        [Input(Guid = "00cb7dc1-edc7-4102-a6a0-6cc44a4f0e56", MappedType = typeof(WidthFXs))]
        public readonly InputSlot<int> WidthFactor = new InputSlot<int>();

        [Input(Guid = "d0919481-203a-4379-8094-187d6209e00d")]
        public readonly InputSlot<float> ShrinkWithDistance = new InputSlot<float>();

        [Input(Guid = "9ba2aa59-e55b-4ebe-aa98-0f79ed77c7aa")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "c10f9c6c-9923-42c6-848d-6b98097acc67")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture_ = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "d90ff4e6-7d70-441f-a064-b40401025c36", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMod = new InputSlot<int>();

        [Input(Guid = "c9cf2182-1297-463c-b5c1-d4ee7ad0895c")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "28f85ae9-ebae-4300-8aa0-738c6327cc44")]
        public readonly InputSlot<float> TransitionProgress = new InputSlot<float>();

        [Input(Guid = "ba83a66f-5a4c-4355-abb2-d4b7cd55d542")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> WrapMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "e797d93b-3847-4324-898e-09018267ea82")]
        public readonly InputSlot<float> UvScale = new InputSlot<float>();

        [Input(Guid = "520c428e-77b0-44d7-95af-d1cbf9b7e4a7", MappedType = typeof(UvModes))]
        public readonly InputSlot<int> UvScaleFX = new InputSlot<int>();

        [Input(Guid = "c78378f5-8306-4586-af6b-99cea40fa9a9")]
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