namespace Skills.Image.I02_Gradients;

[Guid("eea03dc7-ae6c-4a0c-9449-303008643fa9")]
internal sealed class I02m_Vignette : Instance<I02m_Vignette>
{
    [Output(Guid = "5ee55212-0fe7-48eb-8d22-494274876be0")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}