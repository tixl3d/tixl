namespace Lib.image.use;

[Guid("0784b2ea-ac03-4f6e-8de3-4200cb93d651")]
internal sealed class Fxaa : Instance<Fxaa>
{
    [Output(Guid = "68a001ad-f80c-4819-b46c-e626a1c0dbff")]
    public readonly Slot<Texture2D> TextureOutput = new();

        [Input(Guid = "31ea272e-30ec-4af3-835c-5a92af306689")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "aa56d3df-fa8f-4cb5-9164-f2bfd88dffab")]
        public readonly InputSlot<int> Preset = new InputSlot<int>();

        [Input(Guid = "a87cca92-dd43-4845-bd77-9bfec5b75eec")]
        public readonly InputSlot<bool> KeepAlpha = new InputSlot<bool>();
}