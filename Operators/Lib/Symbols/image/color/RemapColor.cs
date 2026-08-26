namespace Lib.image.color;


[Guid("da93f7d1-ef91-4b4a-9708-2d9b1baa4c14")]
internal sealed class RemapColor : Instance<RemapColor>
{
    [Output(Guid = "16e37306-05e1-4de6-babd-80a8d1472a2f")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "876f6f64-7cb4-4060-8571-e0b78b437d41")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "c45d487b-3221-44c7-bf9e-b982a65280f6")]
    public readonly InputSlot<Gradient> Gradient = new InputSlot<Gradient>();

    [Input(Guid = "e3363c0e-819a-45e2-8202-439bcce64d69",MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new InputSlot<int>();

    [Input(Guid = "8a2cc68c-ec38-4bb5-a201-e5c06f0a38d1")]
    public readonly InputSlot<float> Exposure = new InputSlot<float>();

    [Input(Guid = "97771732-56fb-4e0c-915d-c79321ba27b5")]
    public readonly InputSlot<Vector2> GainAndBias = new InputSlot<Vector2>();

    [Input(Guid = "b1763a8b-aa98-4e00-a47c-a5d0d750ae6e")]
    public readonly InputSlot<float> Cycle = new InputSlot<float>();

    [Input(Guid = "eb070a0b-703d-43cc-a877-cf9e371ebd05")]
    public readonly InputSlot<TextureAddressMode> WrapMode = new InputSlot<TextureAddressMode>();

    [Input(Guid = "7777f86d-dbf7-44d4-9da4-99a819038095")]
    public readonly InputSlot<bool> DontColorAlpha = new InputSlot<bool>();

    [Input(Guid = "cb52ff49-17de-4e36-b918-5de6973a234a")]
    public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();

    [Input(Guid = "7023f71c-1c13-4d66-85e7-b0918cb8b02c")]
    public readonly InputSlot<float> Repeat = new InputSlot<float>();

        [Input(Guid = "58f9492f-7866-4fb0-a4d8-1b304f812a6f")]
        public readonly InputSlot<int> GradientSteps = new InputSlot<int>();


    private enum Modes
    {
        UseGrayScale,
        IndividualChannels,
    }
}