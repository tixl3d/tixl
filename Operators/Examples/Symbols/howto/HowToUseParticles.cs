namespace Examples.howto;

[Guid("812561d8-cbb5-40c3-a53e-3c3f0ad2352e")]
internal sealed class HowToUseParticles : Instance<HowToUseParticles>
{

    [Output(Guid = "a6f74a15-1f72-4e9c-955f-0711ff5f9c46")]
    public readonly Slot<Texture2D> Output = new();


}