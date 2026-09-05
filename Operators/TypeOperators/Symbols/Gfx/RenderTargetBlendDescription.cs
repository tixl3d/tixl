namespace Types.Gfx;

[Guid("38ee7546-8d7d-463c-aeea-e482d7ca3f30")]
public sealed class RenderTargetBlendDescription : Instance<RenderTargetBlendDescription>
{
    [Output(Guid = "228E1DC2-944E-4235-BF2D-2EB3F895858C")]
    public readonly Slot<SharpDX.Direct3D11.RenderTargetBlendDescription> Output = new();

    public RenderTargetBlendDescription()
    {
        Output.UpdateAction += Update;
    }


    private void Update(EvaluationContext context)
    {
        Output.Value = new SharpDX.Direct3D11.RenderTargetBlendDescription(BlendEnabled.GetValue(context),
                                                                           SourceBlend.GetValue(context),
                                                                           DestinationBlend.GetValue(context),
                                                                           BlendOperation.GetValue(context),
                                                                           SourceAlphaBlend.GetValue(context),
                                                                           DestinationAlphaBlend.GetValue(context),
                                                                           AlphaBlendOperation.GetValue(context),
                                                                           ColorWriteMaskFlags.All);
        // todo: add color write mask input, enum is byte so input edit needs adjustment
        //                                                            RenderTargetWriteMask.GetValue(context));
    }

    [Input(Guid = "7F535169-8F65-4186-866D-59C2B89D7DA2")]
    public readonly InputSlot<bool> BlendEnabled = new();
    [Input(Guid = "56C398CE-FE71-47EB-A33F-11EEC8F82E79")]
    public readonly InputSlot<BlendOption> SourceBlend = new();
    [Input(Guid = "8DC53FE4-79BB-43E4-9D4A-4E06F9A3214C")]
    public readonly InputSlot<BlendOption> DestinationBlend = new();
    [Input(Guid = "F56E4281-356A-451A-88F1-9CD8AD95D1A5")]
    public readonly InputSlot<BlendOperation> BlendOperation = new();
    [Input(Guid = "2632AF70-5A05-429C-8123-FE280ADEA655")]
    public readonly InputSlot<BlendOption> SourceAlphaBlend = new();
    [Input(Guid = "ACC5550B-18ED-4DBA-8E69-D5228E2AD850")]
    public readonly InputSlot<BlendOption> DestinationAlphaBlend = new();
    [Input(Guid = "01305A3E-54CC-4F6D-8774-F6FF04B4FEC1")]
    public readonly InputSlot<BlendOperation> AlphaBlendOperation = new();
    //        [Input(Guid = "F4A6A615-8558-4FB6-A4A4-142A4C5BD4F3")]
    //        public readonly InputSlot<ColorWriteMaskFlags> RenderTargetWriteMask = new InputSlot<ColorWriteMaskFlags>();
}