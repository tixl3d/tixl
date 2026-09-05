namespace Skills.Image.I01_BasicShapes;

[Guid("0cdb1267-438e-4647-a848-6233d6caa383")]
internal sealed class I01a_WelcomeImage : Instance<I01a_WelcomeImage>
{
    [Output(Guid = "679f1466-bfe5-40d3-8ed0-2ace00e5c793")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}