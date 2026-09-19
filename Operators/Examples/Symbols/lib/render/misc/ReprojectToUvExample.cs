namespace Examples.Lib.render.misc;

[Guid("2bd99e05-9a54-4466-a220-0289939406bd")]
 internal sealed class ReprojectToUvExample : Instance<ReprojectToUvExample>
{
    [Output(Guid = "025e5249-c3fd-4307-8e69-9d4b0c5b9cdf")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}