namespace Examples.Lib.point.sim;

[Guid("af0a4265-44aa-49d9-b674-5b7c1937c99a")]
 internal sealed class TextureMapForceExample : Instance<TextureMapForceExample>
{
    [Output(Guid = "c3c883f9-6f5b-4057-bced-62a1f9a09bb1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}