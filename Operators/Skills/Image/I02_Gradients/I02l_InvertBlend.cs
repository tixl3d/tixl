namespace Skills.Image.I02_Gradients;

[Guid("7741bee3-17cf-4185-a92a-53efeba3da65")]
internal sealed class I02l_InvertBlend : Instance<I02l_InvertBlend>
{
    [Output(Guid = "d04a29e6-8247-4e88-8428-16c1f060ec0f")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}