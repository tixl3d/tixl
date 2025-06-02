namespace Examples.Lib.image.generate.noise;

[Guid("e3c3942b-451e-4e71-b6d8-ca5a6acd7ce1")]
 internal sealed class WorleyNoiseExample : Instance<WorleyNoiseExample>
{
    [Output(Guid = "70d4e316-6d07-413d-a0c4-33714a63cd09")]
    public readonly Slot<Texture2D> ImgOutput = new Slot<Texture2D>();


}