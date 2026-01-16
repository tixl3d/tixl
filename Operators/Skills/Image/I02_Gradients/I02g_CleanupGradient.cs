namespace Skills.Image.I02_Gradients;

[Guid("cdcd9df3-e941-428a-9dd4-7b05266a4bec")]
internal sealed class I02g_CleanupGradient : Instance<I02g_CleanupGradient>
{
    [Output(Guid = "c9157ef7-156c-48b2-af3a-e3392d87679f")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}