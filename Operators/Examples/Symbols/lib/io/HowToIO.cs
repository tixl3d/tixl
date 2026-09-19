namespace Examples.Lib.io;

[Guid("8f61c1b6-3c56-4d5c-8c85-3aaecb71f597")]
internal sealed class HowToIO : Instance<HowToIO>
{
    [Output(Guid = "d09c6594-fbf6-4f03-a8a7-16b990b27d83")]
    public readonly Slot<Texture2D> ImgOutput = new();


}