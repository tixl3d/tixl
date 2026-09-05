namespace Examples.Lib.render.postfx;

[Guid("4b8b7567-a9d2-4956-813d-91e542e1f661")]
 internal sealed class GodRaysExamples : Instance<GodRaysExamples>
{
    [Output(Guid = "6fa518a4-6e57-4d6a-8eb4-539b580c0947")]
    public readonly Slot<Texture2D> ImgOutput = new();


}