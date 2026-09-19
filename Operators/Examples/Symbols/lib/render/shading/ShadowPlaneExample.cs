namespace Examples.Lib.render.shading;

[Guid("e20f492c-490f-4297-a9c8-0e5aab14f9c1")]
 internal sealed class ShadowPlaneExample : Instance<ShadowPlaneExample>
{
    [Output(Guid = "50b1925c-2eca-469d-b5c1-065e01406160")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}