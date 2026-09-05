namespace Examples.Lib.image.generate.pattern;

[Guid("014b8d6f-c7f2-43b5-84a8-033356e440ef")]
 internal sealed class RasterExample : Instance<RasterExample>
{
    [Output(Guid = "8eedd2e2-8806-4e97-9ca2-ec6d881e62fc")]
    public readonly Slot<Texture2D> Output = new();

}