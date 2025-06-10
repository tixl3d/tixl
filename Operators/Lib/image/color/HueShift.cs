namespace Lib.image.color;

[Guid("3c8003e8-70ca-4d71-9294-3df845bbb4a5")]
internal sealed class HueShift : Instance<HueShift>
{
    [Output(Guid = "28128f9d-4bd6-4c68-84e2-369f2e738443")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "258d4260-e693-4c55-a251-fadce351408c")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture2d = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "edbef57d-8d68-4978-8d4d-6d882058f018")]
        public readonly InputSlot<float> Saturation = new InputSlot<float>();

        [Input(Guid = "67548eca-6a4f-4653-8cd3-6b320ec27b59")]
        public readonly InputSlot<float> Hue = new InputSlot<float>();

        [Input(Guid = "b1d28822-af80-4c22-8f18-e22fb16f6dcf")]
        public readonly InputSlot<float> Exposure = new InputSlot<float>();

        [Input(Guid = "b3b65a98-6eba-4a10-89bb-7999385b590c")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> FxTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

}