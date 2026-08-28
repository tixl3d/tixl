namespace Skills.Image.I04_ColorCorrection;

[Guid("ecaf202d-d91d-4bdf-be3f-0447796d8688")]
internal sealed class I04d_TealandOrange :Instance<I04d_TealandOrange>{
    [Output(Guid = "935b01a5-b3f7-4d76-9f90-ce03ff5ca9c7")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}