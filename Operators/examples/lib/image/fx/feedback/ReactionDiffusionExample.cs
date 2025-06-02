namespace Examples.Lib.image.fx.feedback;

[Guid("ddf3077b-6273-4023-88e5-2948312e012b")]
 internal sealed class ReactionDiffusionExample : Instance<ReactionDiffusionExample>
{
    [Output(Guid = "7f8c561d-9683-4504-9e25-61064f7f6345")]
    public readonly Slot<Texture2D> ImgOutput = new();


}