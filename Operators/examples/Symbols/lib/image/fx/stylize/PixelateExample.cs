namespace Examples.Lib.image.fx.stylize;

[Guid("ea7536a2-f69b-4964-a04c-4474bfacfa56")]
 internal sealed class PixelateExample : Instance<PixelateExample>
{
    [Output(Guid = "32ae7d3f-5fcd-474a-8f5e-99ffe28f7b60")]
    public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


}