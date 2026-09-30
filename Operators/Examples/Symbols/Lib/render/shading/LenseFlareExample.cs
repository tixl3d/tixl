namespace Examples.Lib.render.shading;

[Guid("442995fa-3d89-4d6c-b006-77f825f4e3ed")]
 internal sealed class LenseFlareExample : Instance<LenseFlareExample>
{
    [Output(Guid = "d794d1bc-d322-4868-a894-a26ff5ff7805")]
    public readonly Slot<Texture2D> ImgOutput = new();


}