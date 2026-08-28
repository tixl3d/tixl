namespace Lib.image.use;

[Guid("fd5ac393-02a4-43ae-8cdf-64a947abbafa")]
internal sealed class UseTextureReference : Instance<UseTextureReference>
{
    [Output(Guid = "CB9850B3-119A-4839-B79F-A882112503C6")]
    public readonly Slot<RenderTargetReference> Reference = new();
        
    [Output(Guid = "7e846b2f-1d1a-4bfd-bbc8-d71aaaf9b5d4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "FB232E1D-A09B-4B02-8E2F-408CD9AC1FFF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> DepthTexture = new();

        
    public UseTextureReference()
    {
        Texture.UpdateAction += UpdateTexture;
        DepthTexture.UpdateAction += UpdateTexture;
        Reference.UpdateAction += UpdateTexture;
    }
        
    private void UpdateTexture(EvaluationContext context)
    {
        Reference.Value = _renderTargetReference;
        Texture.Value = _renderTargetReference.ColorTexture;
        DepthTexture.Value = _renderTargetReference.DepthTexture;
            
        DepthTexture.DirtyFlag.Clear();
        Texture.DirtyFlag.Clear();
    }

    private readonly RenderTargetReference _renderTargetReference = new();
}