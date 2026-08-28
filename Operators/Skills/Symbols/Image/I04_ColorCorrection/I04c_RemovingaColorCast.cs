namespace Skills.Image.I04_ColorCorrection;

[Guid("17822f0d-0b11-4aaf-88c7-9597f643e23c")]
internal sealed class I04c_RemovingaColorCast :Instance<I04c_RemovingaColorCast>{
    [Output(Guid = "6d2af960-5478-48e5-8463-ede877920d41")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}