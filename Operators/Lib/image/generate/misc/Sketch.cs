namespace Lib.image.generate.misc;

[Guid("8e6ed99c-a3e0-42c0-9f81-a89b1e340757")]
internal sealed class Sketch : Instance<Sketch>
{
    [Output(Guid = "8cedd2ef-75a2-46d9-8a07-02491389a89f")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Output(Guid = "6d0b50be-70d4-4539-8d9d-ebb7434075c2")]
    public readonly Slot<BufferWithViews> Points = new();

        [Input(Guid = "0d965690-5c83-47df-a48f-512e060b5e16")]
        public readonly InputSlot<T3.Core.DataTypes.Command> Scene = new InputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "f823fdfd-fc3d-41c4-bd9d-6badf764d702")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> InputImage = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "2ded8235-157d-486b-a997-87d09d18f998")]
        public readonly InputSlot<string> Filename = new InputSlot<string>();

        [Input(Guid = "1c5d0d86-c000-449e-903a-3212d19d8e1d")]
        public readonly InputSlot<float> StrokeSize = new InputSlot<float>();

        [Input(Guid = "31f3942e-bac5-407f-ad44-6d09920754d9")]
        public readonly InputSlot<System.Numerics.Vector4> StrokeColor = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "5b41e5e9-1cc5-4c26-9b94-433750ef86fc", MappedType = typeof(ColorModes))]
        public readonly InputSlot<int> ColorMode = new InputSlot<int>();
        
        [Input(Guid = "44b88a09-6374-4180-9bc9-713ccfbb36f0")]
        public readonly InputSlot<System.Numerics.Vector4> Background = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "d9931f47-2fc9-4df3-ab97-1a71e45501d2")]
        public readonly InputSlot<bool> ShowOnionSkin = new InputSlot<bool>();

        [Input(Guid = "37093302-053a-47b2-ace6-b9d310d3f4b7")]
        public readonly InputSlot<int> OverridePageIndex = new InputSlot<int>();

        [Input(Guid = "5cdb04d5-9bef-4789-8082-ea04e56b3ca7")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "046a932b-6331-4e97-b289-be88420951cc")]
        public readonly InputSlot<float> OnionSkinRange = new InputSlot<float>();


    private enum ShowModes
    {
        OnlyAtFrame,
        ShowUntilNextFrame,
        WithOnionSkin,
    }
    
    public enum ColorModes
    {
        Stroke,
        Page,
    }
}