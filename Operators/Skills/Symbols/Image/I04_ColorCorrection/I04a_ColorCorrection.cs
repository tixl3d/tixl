namespace Skills.Image.I04_ColorCorrection;

[Guid("b2945c0d-d964-4407-a0dd-162c72c8994b")]
internal sealed class I04a_ColorCorrection :Instance<I04a_ColorCorrection>{
    [Output(Guid = "e542a456-6ff0-47bb-80f8-9879f248df46")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}