namespace Skills.Image.I04_ColorCorrection;

[Guid("d3efe797-8d15-42bc-82cf-12d3bae94ce0")]
internal sealed class I04f_VectorsScope :Instance<I04f_VectorsScope>{
    [Output(Guid = "2853138d-0480-4c41-9874-51c5518a4c82")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}