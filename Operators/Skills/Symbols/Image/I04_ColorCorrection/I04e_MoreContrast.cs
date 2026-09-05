namespace Skills.Image.I04_ColorCorrection;

[Guid("dd1c4895-2ffe-40d3-9d00-f721ccd792bd")]
internal sealed class I04e_MoreContrast :Instance<I04e_MoreContrast>{
    [Output(Guid = "9187e40a-c318-4e3e-89b3-37e37b2f24c4")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}