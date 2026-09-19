namespace Examples.Lib.image.fx.blur;

[Guid("eccd22ed-1a59-4655-b811-10790871cd4c")]
 internal sealed class SharpenExample : Instance<SharpenExample>
{
    [Output(Guid = "edc2dd9c-0a39-42a5-8af5-ea67e639d2f3")]
    public readonly Slot<Texture2D> Texture = new Slot<Texture2D>();


}