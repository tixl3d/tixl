namespace Examples.user.still.there;

[Guid("5ea8bc54-d1f6-4748-9839-e3e4415a5608")]
internal sealed class ThereDemo : Instance<ThereDemo>
{
    [Output(Guid = "9316bc94-c0d3-45a4-9fab-ae9608510b04")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}